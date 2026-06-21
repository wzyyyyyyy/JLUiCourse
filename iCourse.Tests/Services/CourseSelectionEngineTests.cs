using System.Collections.Concurrent;
using System.Net;
using iCourse.Models;
using iCourse.Services;

namespace iCourse.Tests.Services;

public sealed class CourseSelectionEngineTests
{
    [Fact]
    public async Task RunAsync_StartsTwoLanesAndCancelsSiblingOnSuccess()
    {
        var siblingCancelled = NewSignal();
        var bothStarted = NewSignal();
        var calls = 0;
        var transport = new ScriptedTransport(async (_, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 2)
            {
                bothStarted.TrySetResult();
            }

            await bothStarted.Task.WaitAsync(token);
            if (call == 1)
            {
                return Success();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("unreachable");
            }
            catch (OperationCanceledException)
            {
                siblingCancelled.TrySetResult();
                throw;
            }
        });

        var result = await CreateEngine().RunAsync(
            [Course("1")], transport, _ => { }, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(CourseSelectionState.Succeeded, Assert.Single(result).State);
        await siblingCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunAsync_CapacityFullImmediatelyFailsAndCancelsSibling()
    {
        var siblingCancelled = NewSignal();
        var bothStarted = NewSignal();
        var calls = 0;
        var transport = new ScriptedTransport(async (_, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 2)
            {
                bothStarted.TrySetResult();
            }

            await bothStarted.Task.WaitAsync(token);
            if (call == 1)
            {
                return CapacityFull();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("unreachable");
            }
            catch (OperationCanceledException)
            {
                siblingCancelled.TrySetResult();
                throw;
            }
        });

        var result = await CreateEngine().RunAsync(
            [Course("1")], transport, _ => { }, CancellationToken.None);

        var final = Assert.Single(result);
        Assert.Equal(2, calls);
        Assert.Equal(CourseSelectionState.Failed, final.State);
        Assert.Equal("课容量已满", final.LatestResult);
        await siblingCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunAsync_FiftyBlockedCoursesUseExactlyTwentyTransportSlots()
    {
        var release = NewSignal();
        var reachedLimit = NewSignal();
        var active = 0;
        var maxActive = 0;
        var transport = new ScriptedTransport(async (_, token) =>
        {
            var current = Interlocked.Increment(ref active);
            UpdateMax(ref maxActive, current);
            if (current == 20)
            {
                reachedLimit.TrySetResult();
            }

            try
            {
                await release.Task.WaitAsync(token);
                return CapacityFull();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        var courses = Enumerable.Range(1, 50)
            .Select(index => Course(index.ToString()))
            .ToList();
        var run = CreateEngine().RunAsync(courses, transport, _ => { }, CancellationToken.None);

        await reachedLimit.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(20, maxActive);
        release.TrySetResult();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(50, result.Count);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task RunAsync_FiveConsecutiveIdenticalUnknownResponsesFailCourse()
    {
        var calls = 0;
        var transport = new ScriptedTransport((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Unknown("新返回值"));
        });

        var result = await CreateEngine().RunAsync(
            [Course("1")], transport, _ => { }, CancellationToken.None);

        var final = Assert.Single(result);
        Assert.Equal(CourseSelectionState.Failed, final.State);
        Assert.Equal("新返回值", final.LatestResult);
        Assert.InRange(final.AttemptCount, 5, 6);
        Assert.Equal(final.AttemptCount, calls);
    }

    [Fact]
    public async Task RunAsync_CancellationReturnsCancelledAfterEveryTransportStops()
    {
        using var cancellation = new CancellationTokenSource();
        var bothStarted = NewSignal();
        var active = 0;
        var transport = new ScriptedTransport(async (_, token) =>
        {
            if (Interlocked.Increment(ref active) == 2)
            {
                bothStarted.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("unreachable");
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        var run = CreateEngine().RunAsync(
            [Course("1")], transport, _ => { }, cancellation.Token);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(CourseSelectionState.Cancelled, Assert.Single(result).State);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task RunAsync_TransportExceptionRetriesWithoutLeakingSemaphorePermit()
    {
        var calls = 0;
        var active = 0;
        var maxActive = 0;
        var transport = new ScriptedTransport(async (_, _) =>
        {
            var current = Interlocked.Increment(ref active);
            UpdateMax(ref maxActive, current);
            var call = Interlocked.Increment(ref calls);

            try
            {
                await Task.Yield();
                if (call == 1)
                {
                    throw new InvalidOperationException("network down");
                }

                return Success();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        var result = await CreateEngine(maxConcurrency: 1).RunAsync(
            [Course("1")], transport, _ => { }, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(CourseSelectionState.Succeeded, Assert.Single(result).State);
        Assert.Equal(2, calls);
        Assert.Equal(1, maxActive);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task RunAsync_FinalProgressIsLastAndVersionsAreStrictlyIncreasing()
    {
        var events = new ConcurrentQueue<CourseSelectionSnapshot>();
        var siblingCancelled = NewSignal();
        var bothStarted = NewSignal();
        var calls = 0;
        var transport = new ScriptedTransport(async (_, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 2)
            {
                bothStarted.TrySetResult();
            }

            await bothStarted.Task.WaitAsync(token);
            if (call == 1)
            {
                return Success();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("unreachable");
            }
            catch (OperationCanceledException)
            {
                siblingCancelled.TrySetResult();
                throw;
            }
        });

        var result = await CreateEngine().RunAsync(
            [Course("1")], transport, events.Enqueue, CancellationToken.None);

        await siblingCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var published = events.ToArray();
        Assert.NotEmpty(published);
        Assert.Equal(result[0], published[^1]);
        Assert.True(published[^1].IsFinal);
        Assert.All(published[..^1], snapshot => Assert.False(snapshot.IsFinal));
        Assert.True(published.Zip(published.Skip(1), (left, right) => left.Version < right.Version).All(value => value));
    }

    [Fact]
    public async Task RunAsync_EmptyCourseListReturnsEmptyResult()
    {
        var transport = new ScriptedTransport((_, _) => throw new InvalidOperationException("must not be called"));

        var result = await CreateEngine().RunAsync([], transport, _ => { }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public void AggressiveDelay_UsesRequiredRetryBounds()
    {
        var delay = new AggressiveCourseSelectionDelay();

        Assert.All(
            Enumerable.Range(0, 100).Select(_ => delay.GetTransientDelay()),
            value => Assert.InRange(value, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(100)));
        Assert.Equal(TimeSpan.FromMilliseconds(100), delay.GetNetworkDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(1), delay.GetNetworkDelay(100));
        Assert.Equal(TimeSpan.FromMilliseconds(250), delay.GetRateLimitDelay(null, 1));
        Assert.Equal(TimeSpan.FromSeconds(2), delay.GetRateLimitDelay(null, 100));
    }

    [Fact]
    public void AggressiveDelay_PrefersRetryAfter()
    {
        var delay = new AggressiveCourseSelectionDelay();
        var retryAfter = TimeSpan.FromMilliseconds(713);

        Assert.Equal(retryAfter, delay.GetRateLimitDelay(retryAfter, 100));
    }

    [Fact]
    public async Task AggressiveDelay_WaitIsCancellable()
    {
        var delay = new AggressiveCourseSelectionDelay();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => delay.WaitAsync(TimeSpan.FromMinutes(1), cancellation.Token));
    }

    private static CourseSelectionEngine CreateEngine(int maxConcurrency = 20) =>
        new(
            new CourseSelectionResponseClassifier(),
            new ImmediateDelay(),
            new CourseSelectionOptions(2, maxConcurrency, 5));

    private static Course Course(string id) => new()
    {
        CourseId = id,
        Name = $"课程{id}"
    };

    private static CourseSelectionAttempt Success() =>
        new(HttpStatusCode.OK, "{\"code\":200,\"msg\":\"选课成功\"}");

    private static CourseSelectionAttempt CapacityFull() =>
        new(HttpStatusCode.OK, "{\"code\":500,\"msg\":\"课容量已满\"}");

    private static CourseSelectionAttempt Unknown(string message) =>
        new(HttpStatusCode.OK, $"{{\"code\":500,\"msg\":\"{message}\"}}");

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void UpdateMax(ref int target, int value)
    {
        int observed;
        do
        {
            observed = target;
            if (observed >= value)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, observed) != observed);
    }

    private sealed class ImmediateDelay : ICourseSelectionDelay
    {
        public TimeSpan GetTransientDelay() => TimeSpan.Zero;

        public TimeSpan GetNetworkDelay(int failureCount) => TimeSpan.Zero;

        public TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount) =>
            retryAfter ?? TimeSpan.Zero;

        public Task WaitAsync(TimeSpan delay, CancellationToken token) =>
            Task.Delay(TimeSpan.Zero, token);
    }

    private sealed class ScriptedTransport(
        Func<Course, CancellationToken, Task<CourseSelectionAttempt>> handler)
        : ICourseSelectionTransport
    {
        public Task<CourseSelectionAttempt> SendAsync(Course course, CancellationToken token) =>
            handler(course, token);
    }
}
