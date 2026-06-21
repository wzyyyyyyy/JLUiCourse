using System.Collections.Concurrent;
using System.Net;
using iCourse.Models;
using iCourse.Services;

namespace iCourse.Tests.Services;

public sealed class CourseSelectionEngineTests
{
    [Theory]
    [InlineData(3, 20, 5)]
    [InlineData(2, 21, 5)]
    [InlineData(2, 20, 4)]
    public void Constructor_RejectsOptionsOutsideFixedSafetyLimits(
        int lanesPerCourse,
        int maxConcurrency,
        int unknownResponseLimit)
    {
        var options = new CourseSelectionOptions(
            lanesPerCourse,
            maxConcurrency,
            unknownResponseLimit);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CourseSelectionEngine(
                new CourseSelectionResponseClassifier(),
                new ImmediateDelay(),
                options));
    }

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
            [Course("1")], transport, _ => { }, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

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
            [Course("1")], transport, _ => { }, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

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

        var result = await run.WaitAsync(TimeSpan.FromSeconds(2));
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
            [Course("1")], transport, _ => { }, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

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
    public async Task RunAsync_ProgressFaultCancelsSiblingAndEngineCanRunAgain()
    {
        var expected = new InvalidOperationException("progress failed");
        var active = 0;
        var progressCalls = 0;
        var blockingTransport = new ScriptedTransport(async (_, token) =>
        {
            Interlocked.Increment(ref active);
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
        var engine = CreateEngine(maxConcurrency: 1);

        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.RunAsync(
                    [Course("1")],
                    blockingTransport,
                    _ =>
                    {
                        if (Interlocked.Increment(ref progressCalls) == 1)
                        {
                            throw expected;
                        }
                    },
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(1)));

        Assert.Same(expected, fault);
        Assert.Equal(0, active);

        var successful = await engine.RunAsync(
                [Course("2")],
                new ScriptedTransport((_, _) => Task.FromResult(Success())),
                _ => { },
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(CourseSelectionState.Succeeded, Assert.Single(successful).State);
    }

    [Fact]
    public async Task RunAsync_DelayFaultCancelsSiblingAndOtherCoursesBeforePropagating()
    {
        var expected = new InvalidOperationException("delay failed");
        var allStarted = NewSignal();
        var siblingCancelled = NewSignal();
        var otherCourseCancelled = NewSignal();
        var courseOneCalls = 0;
        var otherCourseCancellations = 0;
        var active = 0;
        var transport = new ScriptedTransport(async (course, token) =>
        {
            var call = course.CourseId == "1"
                ? Interlocked.Increment(ref courseOneCalls)
                : 0;
            if (Interlocked.Increment(ref active) == 4)
            {
                allStarted.TrySetResult();
            }

            try
            {
                await allStarted.Task.WaitAsync(token);
                if (course.CourseId == "1" && call == 1)
                {
                    return Busy();
                }

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    throw new InvalidOperationException("unreachable");
                }
                catch (OperationCanceledException)
                {
                    if (course.CourseId == "1")
                    {
                        siblingCancelled.TrySetResult();
                    }
                    else if (Interlocked.Increment(ref otherCourseCancellations) == 2)
                    {
                        otherCourseCancelled.TrySetResult();
                    }

                    throw;
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        var engine = new CourseSelectionEngine(
            new CourseSelectionResponseClassifier(),
            new ThrowingDelay(expected),
            new CourseSelectionOptions());

        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.RunAsync(
                    [Course("1"), Course("2")],
                    transport,
                    _ => { },
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(1)));

        Assert.Same(expected, fault);
        await siblingCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await otherCourseCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
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
            [Course("1")], transport, events.Enqueue, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        await siblingCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var published = events.ToArray();
        Assert.NotEmpty(published);
        Assert.Equal(result[0], published[^1]);
        Assert.True(published[^1].IsFinal);
        Assert.All(published[..^1], snapshot => Assert.False(snapshot.IsFinal));
        Assert.True(published.Zip(published.Skip(1), (left, right) => left.Version < right.Version).All(value => value));
    }

    [Fact]
    public async Task RunAsync_RetryProgressTracksInFlightRequestsAndEndsWithCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var events = new ConcurrentQueue<CourseSelectionSnapshot>();
        var delay = new BlockingDelay();
        var bothStarted = NewSignal();
        var releaseFirst = NewSignal();
        var releaseSecond = NewSignal();
        var active = 0;
        var calls = 0;
        var transport = new ScriptedTransport(async (_, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            Interlocked.Increment(ref active);
            if (call == 2)
            {
                bothStarted.TrySetResult();
            }

            try
            {
                await bothStarted.Task.WaitAsync(token);
                await (call == 1 ? releaseFirst.Task : releaseSecond.Task).WaitAsync(token);
                return Busy();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        var run = CreateEngine(delay: delay).RunAsync(
            [Course("1")], transport, events.Enqueue, cancellation.Token);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        releaseFirst.TrySetResult();
        await delay.FirstWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, active);
        Assert.Equal(
            CourseSelectionState.Racing,
            events.Last(snapshot => snapshot.LatestResult == "系统繁忙").State);

        releaseSecond.TrySetResult();
        await delay.BothWaitsStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, active);
        Assert.Equal(
            CourseSelectionState.BackingOff,
            events.Last(snapshot => snapshot.LatestResult == "系统繁忙").State);

        cancellation.Cancel();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(2));

        var published = events.ToArray();
        var final = Assert.Single(result);
        Assert.Equal(CourseSelectionState.Cancelled, final.State);
        Assert.Equal(final, published[^1]);
        Assert.True(published.Zip(
            published.Skip(1),
            (left, right) => left.Version < right.Version).All(value => value));
        Assert.All(published, snapshot => Assert.True(snapshot.Elapsed >= TimeSpan.Zero));
        Assert.All(published[..^1], snapshot => Assert.True(final.Elapsed >= snapshot.Elapsed));
    }

    [Fact]
    public async Task RunAsync_ConcurrentRetriesPublishMonotonicSnapshots()
    {
        const int rounds = 12;
        const int courseCount = 12;
        const int attemptsBeforeSuccess = 60;

        for (var round = 0; round < rounds; round++)
        {
            var attempts = new ConcurrentDictionary<string, int>();
            var events = new ConcurrentDictionary<string, ConcurrentQueue<CourseSelectionSnapshot>>();
            var transport = new ScriptedTransport(async (course, token) =>
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                var attempt = attempts.AddOrUpdate(course.CourseId, 1, (_, current) => current + 1);
                return attempt >= attemptsBeforeSuccess ? Success() : Busy();
            });
            var courses = Enumerable.Range(1, courseCount)
                .Select(index => Course($"{round}-{index}"))
                .ToList();

            var result = await new CourseSelectionEngine(
                    new CourseSelectionResponseClassifier(),
                    new YieldingDelay(),
                    new CourseSelectionOptions())
                .RunAsync(
                    courses,
                    transport,
                    snapshot =>
                    {
                        Thread.Yield();
                        events.GetOrAdd(
                            snapshot.CourseId,
                            _ => new ConcurrentQueue<CourseSelectionSnapshot>()).Enqueue(snapshot);
                    },
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2));

            Assert.All(result, final =>
            {
                var published = events[final.CourseId].ToArray();
                Assert.Equal(final, published[^1]);
                Assert.Equal(published, published.OrderBy(snapshot => snapshot.Version));
                Assert.All(
                    published.Zip(published.Skip(1)),
                    pair => Assert.True(pair.First.AttemptCount <= pair.Second.AttemptCount));

                foreach (var pair in published.Zip(published.Skip(1)))
                {
                    if (pair.First.State == CourseSelectionState.BackingOff &&
                        pair.Second.State == CourseSelectionState.Racing)
                    {
                        Assert.True(pair.Second.AttemptCount > pair.First.AttemptCount);
                    }
                }
            });
        }
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-200)]
    public void AggressiveDelay_NonPositiveRetryAfterUsesNormalBackoff(int milliseconds)
    {
        var delay = new AggressiveCourseSelectionDelay();

        var result = delay.GetRateLimitDelay(
            TimeSpan.FromMilliseconds(milliseconds),
            failureCount: 1);

        Assert.Equal(TimeSpan.FromMilliseconds(250), result);
    }

    [Fact]
    public void AggressiveDelay_UnsupportedLargeRetryAfterUsesNormalBackoff()
    {
        var delay = new AggressiveCourseSelectionDelay();

        var result = delay.GetRateLimitDelay(TimeSpan.MaxValue, failureCount: 1);

        Assert.Equal(TimeSpan.FromMilliseconds(250), result);
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

    private static CourseSelectionEngine CreateEngine(
        int maxConcurrency = 20,
        ICourseSelectionDelay? delay = null) =>
        new(
            new CourseSelectionResponseClassifier(),
            delay ?? new ImmediateDelay(),
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

    private static CourseSelectionAttempt Busy() =>
        new(HttpStatusCode.OK, "{\"code\":500,\"msg\":\"系统繁忙\"}");

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

    private sealed class BlockingDelay : ICourseSelectionDelay
    {
        private int waits;

        public TaskCompletionSource FirstWaitStarted { get; } = NewSignal();

        public TaskCompletionSource BothWaitsStarted { get; } = NewSignal();

        public TimeSpan GetTransientDelay() => TimeSpan.Zero;

        public TimeSpan GetNetworkDelay(int failureCount) => TimeSpan.Zero;

        public TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount) =>
            retryAfter ?? TimeSpan.Zero;

        public Task WaitAsync(TimeSpan delay, CancellationToken token)
        {
            var count = Interlocked.Increment(ref waits);
            FirstWaitStarted.TrySetResult();
            if (count == 2)
            {
                BothWaitsStarted.TrySetResult();
            }

            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }

    private sealed class ThrowingDelay(Exception exception) : ICourseSelectionDelay
    {
        public TimeSpan GetTransientDelay() => TimeSpan.Zero;

        public TimeSpan GetNetworkDelay(int failureCount) => TimeSpan.Zero;

        public TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount) =>
            TimeSpan.Zero;

        public Task WaitAsync(TimeSpan delay, CancellationToken token) =>
            Task.FromException(exception);
    }

    private sealed class YieldingDelay : ICourseSelectionDelay
    {
        public TimeSpan GetTransientDelay() => TimeSpan.Zero;

        public TimeSpan GetNetworkDelay(int failureCount) => TimeSpan.Zero;

        public TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount) =>
            TimeSpan.Zero;

        public async Task WaitAsync(TimeSpan delay, CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
        }
    }

    private sealed class ScriptedTransport(
        Func<Course, CancellationToken, Task<CourseSelectionAttempt>> handler)
        : ICourseSelectionTransport
    {
        public Task<CourseSelectionAttempt> SendAsync(Course course, CancellationToken token) =>
            handler(course, token);
    }
}
