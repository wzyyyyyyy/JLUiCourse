using System.Net;
using System.Text.RegularExpressions;
using iCourse.Models;
using Newtonsoft.Json.Linq;

namespace iCourse.Services;

public sealed class CourseSelectionResponseClassifier
{
    private const int MaxReasonLength = 120;
    private const string CapacityFullFragment = "课容量已满";

    private static readonly Regex HtmlTagPattern = new(
        "<[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] RateLimitFragments =
    [
        "请求过于频繁",
        "操作频繁",
        "稍后再试"
    ];

    private static readonly string[] PermanentFailureFragments =
    [
        "时间冲突",
        "不符合",
        "不可选",
        "无效",
        "批次已结束",
        "资格不符",
        "无选课资格",
        "没有选课资格",
        "不具备选课资格",
        "学分上限"
    ];

    private static readonly string[] TransientFragments =
    [
        "尚未开始",
        "未到选课时间",
        "系统繁忙",
        "系统异常",
        "处理中"
    ];

    public CourseSelectionClassification Classify(CourseSelectionAttempt attempt)
    {
        var isJson = TryParseJson(attempt.Body, out var json);
        var message = json?["msg"]?.ToString() ?? "服务器未返回原因";
        var capacitySource = isJson ? message : attempt.Body;

        if (capacitySource.Contains(CapacityFullFragment, StringComparison.Ordinal))
        {
            return new(
                CourseSelectionDecision.TerminalFailure,
                CapacityFullFragment);
        }

        if (!string.IsNullOrWhiteSpace(attempt.Error))
        {
            return new(CourseSelectionDecision.Retry, attempt.Error);
        }

        if (attempt.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new(
                CourseSelectionDecision.RateLimited,
                "请求受限",
                attempt.RetryAfter);
        }

        if (attempt.StatusCode == HttpStatusCode.RequestTimeout)
        {
            return new(CourseSelectionDecision.Retry, "请求超时");
        }

        if (attempt.StatusCode is { } statusCode && (int)statusCode is >= 500 and <= 599)
        {
            return new(
                CourseSelectionDecision.Retry,
                $"服务器暂时不可用 ({(int)statusCode})");
        }

        if (json is null)
        {
            return new(
                CourseSelectionDecision.Retry,
                SummarizeUnknownResponse(attempt.Body),
                IsUnknown: true);
        }

        _ = int.TryParse(json["code"]?.ToString(), out var code);

        if (code == 200 || message.Contains("已在选课结果中", StringComparison.Ordinal))
        {
            return new(CourseSelectionDecision.Success, message);
        }

        if (ContainsAny(message, RateLimitFragments))
        {
            return new(
                CourseSelectionDecision.RateLimited,
                message,
                attempt.RetryAfter);
        }

        if (ContainsAny(message, PermanentFailureFragments))
        {
            return new(CourseSelectionDecision.TerminalFailure, message);
        }

        if (ContainsAny(message, TransientFragments))
        {
            return new(CourseSelectionDecision.Retry, message);
        }

        return new(
            CourseSelectionDecision.Retry,
            SummarizeUnknownResponse(message),
            IsUnknown: true);
    }

    private static bool ContainsAny(string message, IEnumerable<string> fragments) =>
        fragments.Any(fragment => message.Contains(fragment, StringComparison.Ordinal));

    private static bool TryParseJson(string response, out JObject? json)
    {
        try
        {
            json = JToken.Parse(response) as JObject;
            return true;
        }
        catch
        {
            json = null;
            return false;
        }
    }

    private static string SummarizeUnknownResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return "空响应";
        }

        var withoutTags = HtmlTagPattern.Replace(response, " ");
        var readable = WebUtility.HtmlDecode(withoutTags);
        readable = WhitespacePattern.Replace(readable, " ").Trim();

        if (readable.Length == 0)
        {
            return "无法识别的服务器响应";
        }

        return readable.Length <= MaxReasonLength
            ? readable
            : $"{readable[..(MaxReasonLength - 1)]}…";
    }
}
