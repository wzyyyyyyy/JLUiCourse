using System.Net;
using iCourse.Models;
using iCourse.Services;

namespace iCourse.Tests.Services;

public sealed class CourseSelectionResponseClassifierTests
{
    private readonly CourseSelectionResponseClassifier classifier = new();

    [Fact]
    public void Classify_Code200_ReturnsSuccess()
    {
        var result = classifier.Classify(Attempt("{\"code\":200,\"msg\":\"选课成功\"}"));

        Assert.Equal(CourseSelectionDecision.Success, result.Decision);
        Assert.Equal("选课成功", result.Reason);
    }

    [Fact]
    public void Classify_AlreadySelectedMessage_ReturnsSuccess()
    {
        var result = classifier.Classify(Attempt("{\"code\":500,\"msg\":\"该课程已在选课结果中\"}"));

        Assert.Equal(CourseSelectionDecision.Success, result.Decision);
    }

    [Theory]
    [InlineData("课容量已满")]
    [InlineData("课程时间冲突")]
    [InlineData("不符合选课条件")]
    [InlineData("该课程不可选")]
    [InlineData("课程信息无效")]
    [InlineData("选课批次已结束")]
    [InlineData("没有选课资格")]
    [InlineData("已达到学分上限")]
    public void Classify_PermanentBusinessMessage_ReturnsTerminalFailure(string message)
    {
        var result = classifier.Classify(Attempt(JsonMessage(message)));

        Assert.Equal(CourseSelectionDecision.TerminalFailure, result.Decision);
        Assert.Equal(message, result.Reason);
    }

    [Theory]
    [InlineData("选课尚未开始")]
    [InlineData("未到选课时间")]
    [InlineData("系统繁忙")]
    [InlineData("系统异常")]
    [InlineData("请求处理中")]
    public void Classify_TransientBusinessMessage_ReturnsRetry(string message)
    {
        var result = classifier.Classify(Attempt(JsonMessage(message)));

        Assert.Equal(CourseSelectionDecision.Retry, result.Decision);
        Assert.False(result.IsUnknown);
    }

    [Theory]
    [InlineData("请求过于频繁")]
    [InlineData("操作频繁")]
    [InlineData("请稍后再试")]
    public void Classify_RateLimitBusinessMessage_ReturnsRateLimited(string message)
    {
        var result = classifier.Classify(Attempt(JsonMessage(message)));

        Assert.Equal(CourseSelectionDecision.RateLimited, result.Decision);
        Assert.Equal(message, result.Reason);
    }

    [Fact]
    public void Classify_Http429_PreservesRetryAfter()
    {
        var retryAfter = TimeSpan.FromMilliseconds(750);
        var attempt = new CourseSelectionAttempt(HttpStatusCode.TooManyRequests, "", retryAfter);

        var result = classifier.Classify(attempt);

        Assert.Equal(CourseSelectionDecision.RateLimited, result.Decision);
        Assert.Equal(retryAfter, result.RetryAfter);
    }

    [Fact]
    public void Classify_NetworkError_ReturnsRetryWithErrorReason()
    {
        var attempt = new CourseSelectionAttempt(null, "", Error: "连接被重置");

        var result = classifier.Classify(attempt);

        Assert.Equal(CourseSelectionDecision.Retry, result.Decision);
        Assert.Equal("连接被重置", result.Reason);
        Assert.False(result.IsUnknown);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Classify_TransientHttpStatus_ReturnsRetry(HttpStatusCode statusCode)
    {
        var result = classifier.Classify(new CourseSelectionAttempt(statusCode, ""));

        Assert.Equal(CourseSelectionDecision.Retry, result.Decision);
        Assert.False(result.IsUnknown);
    }

    [Fact]
    public void Classify_UnknownJsonMessage_ReturnsUnknownRetryWithMessageReason()
    {
        var result = classifier.Classify(Attempt("{\"code\":500,\"msg\":\"新返回值\"}"));

        Assert.Equal(CourseSelectionDecision.Retry, result.Decision);
        Assert.True(result.IsUnknown);
        Assert.Equal("新返回值", result.Reason);
    }

    [Fact]
    public void Classify_NonJsonResponse_ReturnsUnknownRetryWithReadableText()
    {
        var result = classifier.Classify(Attempt("  网关返回了未知内容  "));

        Assert.Equal(CourseSelectionDecision.Retry, result.Decision);
        Assert.True(result.IsUnknown);
        Assert.Equal("网关返回了未知内容", result.Reason);
    }

    [Fact]
    public void Classify_EmptyResponse_ReturnsUnknownRetryWithReadableReason()
    {
        var result = classifier.Classify(Attempt(""));

        Assert.Equal(CourseSelectionDecision.Retry, result.Decision);
        Assert.True(result.IsUnknown);
        Assert.Equal("空响应", result.Reason);
    }

    private static CourseSelectionAttempt Attempt(string body) =>
        new(HttpStatusCode.OK, body);

    private static string JsonMessage(string message) =>
        $"{{\"code\":500,\"msg\":\"{message}\"}}";
}
