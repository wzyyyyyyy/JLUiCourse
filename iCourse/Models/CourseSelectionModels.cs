using System.Net;

namespace iCourse.Models;

public enum CourseSelectionState
{
    Waiting,
    Racing,
    BackingOff,
    Succeeded,
    Failed,
    Cancelled
}

public enum CourseSelectionDecision
{
    Success,
    TerminalFailure,
    Retry,
    RateLimited
}

public sealed record CourseSelectionAttempt(
    HttpStatusCode? StatusCode,
    string Body,
    TimeSpan? RetryAfter = null,
    string? Error = null);

public sealed record CourseSelectionClassification(
    CourseSelectionDecision Decision,
    string Reason,
    TimeSpan? RetryAfter = null,
    bool IsUnknown = false);

public sealed record CourseSelectionSnapshot(
    string CourseId,
    string CourseName,
    CourseSelectionState State,
    int AttemptCount,
    TimeSpan Elapsed,
    string LatestResult,
    long Version)
{
    public bool IsFinal => State is CourseSelectionState.Succeeded
        or CourseSelectionState.Failed
        or CourseSelectionState.Cancelled;

    public static CourseSelectionSnapshot Waiting(Course course) =>
        new(
            course.CourseId,
            course.Name,
            CourseSelectionState.Waiting,
            0,
            TimeSpan.Zero,
            "等待开始",
            0);
}

public sealed record CourseSelectionOptions(
    int LanesPerCourse = 2,
    int MaxConcurrency = 20,
    int UnknownResponseLimit = 5);
