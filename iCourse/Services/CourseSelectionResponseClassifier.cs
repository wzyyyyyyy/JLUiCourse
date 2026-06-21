using System.Net;
using iCourse.Models;
using Newtonsoft.Json.Linq;

namespace iCourse.Services;

public sealed class CourseSelectionResponseClassifier
{
    private static readonly string[] RateLimitFragments =
    [
        "请求过于频繁",
        "操作频繁",
        "稍后再试"
    ];

    private static readonly string[] PermanentFailureFragments =
    [
        "课容量已满",
        "时间冲突",
        "不符合",
        "不可选",
        "无效",
        "批次已结束",
        "资格",
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

        string message;
        int code;

        try
        {
            var json = JObject.Parse(attempt.Body);
            code = json["code"]?.ToObject<int>() ?? 0;
            message = json["msg"]?.ToString() ?? "服务器未返回原因";
        }
        catch
        {
            var readableBody = string.IsNullOrWhiteSpace(attempt.Body)
                ? "空响应"
                : attempt.Body.Trim();

            return new(
                CourseSelectionDecision.Retry,
                readableBody,
                IsUnknown: true);
        }

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
            message,
            IsUnknown: true);
    }

    private static bool ContainsAny(string message, IEnumerable<string> fragments) =>
        fragments.Any(fragment => message.Contains(fragment, StringComparison.Ordinal));
}
