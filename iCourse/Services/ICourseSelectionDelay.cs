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
    public TimeSpan GetTransientDelay() =>
        TimeSpan.FromMilliseconds(Random.Shared.Next(40, 101));

    public TimeSpan GetNetworkDelay(int failureCount) =>
        ExponentialDelay(
            TimeSpan.FromMilliseconds(100),
            failureCount,
            TimeSpan.FromSeconds(1));

    public TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount) =>
        retryAfter is { } requested && requested > TimeSpan.Zero
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
