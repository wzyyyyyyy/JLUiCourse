using iCourse.Services;

namespace iCourse.Tests.Services;

public sealed class CourseSelectionRunGuardTests
{
    [Fact]
    public void TryBegin_AllowsOnlyOneActiveRunUntilComplete()
    {
        var guard = new CourseSelectionRunGuard();

        Assert.True(guard.TryBegin(out var first));
        Assert.False(first.IsCancellationRequested);
        Assert.False(guard.TryBegin(out _));

        guard.Complete();

        Assert.True(guard.TryBegin(out var second));
        Assert.False(second.IsCancellationRequested);
        guard.Complete();
    }

    [Fact]
    public void Cancel_CancelsActiveTokenAndCanBeRepeatedSafely()
    {
        var guard = new CourseSelectionRunGuard();
        Assert.True(guard.TryBegin(out var token));

        guard.Cancel();
        guard.Cancel();

        Assert.True(token.IsCancellationRequested);
        guard.Complete();
        guard.Complete();
        guard.Cancel();
    }

    [Fact]
    public void Complete_AfterCancellationAllowsFreshRun()
    {
        var guard = new CourseSelectionRunGuard();
        Assert.True(guard.TryBegin(out var cancelled));
        guard.Cancel();
        guard.Complete();

        Assert.True(guard.TryBegin(out var fresh));
        Assert.True(cancelled.IsCancellationRequested);
        Assert.False(fresh.IsCancellationRequested);
        guard.Complete();
    }

    [Fact]
    public void Complete_DisposesCompletedSourceAndLeavesNextRunUsable()
    {
        var guard = new CourseSelectionRunGuard();
        Assert.True(guard.TryBegin(out var completed));
        _ = completed.WaitHandle;

        guard.Complete();
        guard.Complete();

        Assert.Throws<ObjectDisposedException>(() => _ = completed.WaitHandle);
        Assert.True(guard.TryBegin(out var fresh));
        Assert.False(fresh.IsCancellationRequested);
        guard.Cancel();
        Assert.True(fresh.IsCancellationRequested);
        guard.Complete();
    }

    [Fact]
    public async Task TryBegin_ConcurrentRaceHasExactlyOneWinner()
    {
        var guard = new CourseSelectionRunGuard();
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 64)
            .Select(index => Task.Run(() =>
            {
                start.Wait();
                return guard.TryBegin(out _);
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(result => result));
        guard.Complete();
    }
}
