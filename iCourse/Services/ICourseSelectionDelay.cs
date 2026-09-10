using iCourse.Models;

namespace iCourse.Services;

public interface ICourseSelectionDelay
{
    TimeSpan GetTransientDelay();

    TimeSpan GetNetworkDelay(int failureCount);

    TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount);

    Task WaitAsync(TimeSpan delay, CancellationToken token);
}

public sealed class AggressiveCourseSelectionDelay : ICourseSelectionDelay
{
    private const int RushMinDelayMs = 40;
    private const int RushMaxDelayMs = 100;
    private const int ScavengeMinDelayMs = 200;
    private const int ScavengeMaxDelayMs = 500;

    private static readonly TimeSpan MaximumTaskDelay =
        TimeSpan.FromMilliseconds(4_294_967_294);

    private readonly CourseSelectionModeState mode;

    public AggressiveCourseSelectionDelay(CourseSelectionModeState? mode = null)
    {
        this.mode = mode ?? new CourseSelectionModeState();
    }

    public TimeSpan GetTransientDelay() =>
        TimeSpan.FromMilliseconds(
            mode.IsScavenge
                ? Random.Shared.Next(ScavengeMinDelayMs, ScavengeMaxDelayMs + 1)
                : Random.Shared.Next(RushMinDelayMs, RushMaxDelayMs + 1));

    public TimeSpan GetNetworkDelay(int failureCount) =>
        ExponentialDelay(
            TimeSpan.FromMilliseconds(100),
            failureCount,
            TimeSpan.FromSeconds(1));

    public TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount) =>
        retryAfter is { } requested &&
        requested > TimeSpan.Zero &&
        requested <= MaximumTaskDelay
            ? requested
            : ExponentialDelay(
                TimeSpan.FromMilliseconds(250),
                failureCount,
                TimeSpan.FromSeconds(2));

    public Task WaitAsync(TimeSpan delay, CancellationToken token) =>
        Task.Delay(delay, token);

    private static TimeSpan ExponentialDelay(
        TimeSpan initial,
        int failureCount,
        TimeSpan maximum)
    {
        var milliseconds = initial.TotalMilliseconds;
        var retries = Math.Max(1, failureCount);
        for (var retry = 1; retry < retries && milliseconds < maximum.TotalMilliseconds; retry++)
        {
            milliseconds = Math.Min(maximum.TotalMilliseconds, milliseconds * 2);
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
