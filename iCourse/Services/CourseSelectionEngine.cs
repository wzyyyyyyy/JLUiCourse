using System.Diagnostics;
using System.Runtime.ExceptionServices;
using iCourse.Models;

namespace iCourse.Services;

public sealed class CourseSelectionEngine
{
    private readonly CourseSelectionResponseClassifier classifier;
    private readonly ICourseSelectionDelay delay;
    private readonly CourseSelectionOptions options;

    public CourseSelectionEngine(
        CourseSelectionResponseClassifier classifier,
        ICourseSelectionDelay delay,
        CourseSelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(options);

        if (options.LanesPerCourse != 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Exactly two lanes are required.");
        }

        if (options.MaxConcurrency is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Concurrency must be between 1 and 20.");
        }

        if (options.UnknownResponseLimit != 5)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown-response limit must be five.");
        }

        this.classifier = classifier;
        this.delay = delay;
        this.options = options;
    }

    public async Task<IReadOnlyList<CourseSelectionSnapshot>> RunAsync(
        IReadOnlyList<Course> courses,
        ICourseSelectionTransport transport,
        Action<CourseSelectionSnapshot> progress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(courses);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(progress);

        if (courses.Count == 0)
        {
            return Array.Empty<CourseSelectionSnapshot>();
        }

        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var limiter = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        var runFault = new FirstFault();
        var courseTasks = new Task<CourseSelectionSnapshot>[courses.Count];
        for (var index = 0; index < courses.Count; index++)
        {
            courseTasks[index] = RunCourseWithFaultCancellationAsync(
                courses[index],
                transport,
                limiter,
                progress,
                runCancellation,
                runFault);
        }

        CourseSelectionSnapshot[]? results = null;
        try
        {
            results = await Task.WhenAll(courseTasks).ConfigureAwait(false);
        }
        catch (Exception) when (runFault.HasFault)
        {
            // The first non-cancellation fault is rethrown after every course is clean.
        }
        finally
        {
            runCancellation.Cancel();
            await ObserveCleanupAsync(Task.WhenAll(courseTasks)).ConfigureAwait(false);
        }

        runFault.ThrowIfCaptured();
        return results ?? throw new InvalidOperationException("Selection run completed without results.");
    }

    private async Task<CourseSelectionSnapshot> RunCourseWithFaultCancellationAsync(
        Course course,
        ICourseSelectionTransport transport,
        SemaphoreSlim limiter,
        Action<CourseSelectionSnapshot> progress,
        CancellationTokenSource runCancellation,
        FirstFault runFault)
    {
        try
        {
            return await RunCourseAsync(
                course,
                transport,
                limiter,
                progress,
                runCancellation,
                runFault).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            runFault.Capture(exception);
            runCancellation.Cancel();
            throw;
        }
    }

    private async Task<CourseSelectionSnapshot> RunCourseAsync(
        Course course,
        ICourseSelectionTransport transport,
        SemaphoreSlim limiter,
        Action<CourseSelectionSnapshot> progress,
        CancellationTokenSource runCancellation,
        FirstFault runFault)
    {
        using var courseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            runCancellation.Token);
        var runtime = new CourseRuntime(course);
        var progressPublisher = new ProgressPublisher(progress);
        var courseFault = new FirstFault();
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lanes = new Task[options.LanesPerCourse];

        for (var lane = 0; lane < lanes.Length; lane++)
        {
            lanes[lane] = RunLaneWithFaultCancellationAsync(
                runtime,
                progressPublisher,
                transport,
                limiter,
                startSignal.Task,
                courseCancellation,
                courseFault,
                runCancellation,
                runFault);
        }

        startSignal.TrySetResult();

        try
        {
            await Task.WhenAll(lanes).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (courseCancellation.IsCancellationRequested)
        {
            // A winning lane or the caller cancelled every outstanding lane.
        }
        catch (Exception) when (courseFault.HasFault)
        {
            // The first lane fault is rethrown after its sibling has stopped.
        }
        finally
        {
            courseCancellation.Cancel();
            await ObserveCleanupAsync(Task.WhenAll(lanes)).ConfigureAwait(false);
        }

        courseFault.ThrowIfCaptured();
        return progressPublisher.Publish(runtime.GetFinalForPublication);
    }

    private async Task RunLaneWithFaultCancellationAsync(
        CourseRuntime runtime,
        ProgressPublisher progressPublisher,
        ICourseSelectionTransport transport,
        SemaphoreSlim limiter,
        Task startSignal,
        CancellationTokenSource courseCancellation,
        FirstFault courseFault,
        CancellationTokenSource runCancellation,
        FirstFault runFault)
    {
        try
        {
            await RunLaneAsync(
                runtime,
                progressPublisher,
                transport,
                limiter,
                startSignal,
                courseCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (courseCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            courseFault.Capture(exception);
            runFault.Capture(exception);
            courseCancellation.Cancel();
            runCancellation.Cancel();
            throw;
        }
    }

    private async Task RunLaneAsync(
        CourseRuntime runtime,
        ProgressPublisher progressPublisher,
        ICourseSelectionTransport transport,
        SemaphoreSlim limiter,
        Task startSignal,
        CancellationTokenSource courseCancellation)
    {
        var token = courseCancellation.Token;
        var retryCount = 0;
        await startSignal.ConfigureAwait(false);

        while (true)
        {
            await limiter.WaitAsync(token).ConfigureAwait(false);
            var beganAttempt = false;
            TimeSpan wait;

            try
            {
                if (!progressPublisher.TryPublish(
                        () =>
                        {
                            if (!runtime.TryBeginAttempt(out var racing))
                            {
                                return null;
                            }

                            beganAttempt = true;
                            return racing;
                        }))
                {
                    return;
                }

                CourseSelectionAttempt attempt;
                try
                {
                    attempt = await transport.SendAsync(runtime.Course, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    attempt = new CourseSelectionAttempt(null, "", Error: exception.Message);
                }
                finally
                {
                    progressPublisher.Execute(runtime.EndAttempt);
                    beganAttempt = false;
                }

                var classification = classifier.Classify(attempt);
                var outcome = progressPublisher.Execute(() =>
                    runtime.ProcessClassification(
                        classification,
                        options.UnknownResponseLimit));

                if (outcome == ClassificationOutcome.Finalized)
                {
                    courseCancellation.Cancel();
                    return;
                }

                if (outcome == ClassificationOutcome.AlreadyFinal)
                {
                    return;
                }

                retryCount++;
                wait = classification.Decision == CourseSelectionDecision.RateLimited
                    ? delay.GetRateLimitDelay(classification.RetryAfter, retryCount)
                    : attempt.Error is null
                        ? delay.GetTransientDelay()
                        : delay.GetNetworkDelay(retryCount);

                if (!progressPublisher.TryPublish(
                        () => runtime.TryCreateRetrySnapshot(
                            classification.Reason,
                            out var retrying)
                            ? retrying
                            : null))
                {
                    return;
                }
            }
            finally
            {
                if (beganAttempt)
                {
                    progressPublisher.Execute(runtime.EndAttempt);
                }

                limiter.Release();
            }

            await delay.WaitAsync(wait, token).ConfigureAwait(false);
        }
    }

    private static async Task ObserveCleanupAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Faults are captured before cancellation and rethrown after cleanup.
        }
    }

    private enum ClassificationOutcome
    {
        Continue,
        Finalized,
        AlreadyFinal
    }

    private sealed class CourseRuntime(Course course)
    {
        private readonly object sync = new();
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private int attempts;
        private int inFlight;
        private string? unknownReason;
        private int unknownCount;
        private CourseSelectionSnapshot? finalSnapshot;
        private bool finalPublished;

        public Course Course { get; } = course;

        public bool TryBeginAttempt(out CourseSelectionSnapshot snapshot)
        {
            lock (sync)
            {
                if (finalSnapshot is not null)
                {
                    snapshot = null!;
                    return false;
                }

                attempts++;
                inFlight++;
                snapshot = CreateSnapshot(CourseSelectionState.Racing, "正在尝试");
                return true;
            }
        }

        public void EndAttempt()
        {
            lock (sync)
            {
                inFlight--;
            }
        }

        public ClassificationOutcome ProcessClassification(
            CourseSelectionClassification classification,
            int unknownResponseLimit)
        {
            lock (sync)
            {
                if (finalSnapshot is not null)
                {
                    return ClassificationOutcome.AlreadyFinal;
                }

                if (classification.IsUnknown)
                {
                    unknownCount = string.Equals(
                        unknownReason,
                        classification.Reason,
                        StringComparison.Ordinal)
                        ? unknownCount + 1
                        : 1;
                    unknownReason = classification.Reason;

                    if (unknownCount >= unknownResponseLimit)
                    {
                        finalSnapshot = CreateSnapshot(
                            CourseSelectionState.Failed,
                            classification.Reason);
                        return ClassificationOutcome.Finalized;
                    }
                }
                else
                {
                    unknownReason = null;
                    unknownCount = 0;
                }

                if (classification.Decision == CourseSelectionDecision.Success)
                {
                    finalSnapshot = CreateSnapshot(
                        CourseSelectionState.Succeeded,
                        classification.Reason);
                    return ClassificationOutcome.Finalized;
                }

                if (classification.Decision == CourseSelectionDecision.TerminalFailure)
                {
                    finalSnapshot = CreateSnapshot(
                        CourseSelectionState.Failed,
                        classification.Reason);
                    return ClassificationOutcome.Finalized;
                }

                return ClassificationOutcome.Continue;
            }
        }

        public bool TryCreateRetrySnapshot(
            string reason,
            out CourseSelectionSnapshot snapshot)
        {
            lock (sync)
            {
                if (finalSnapshot is not null)
                {
                    snapshot = null!;
                    return false;
                }

                var state = inFlight > 0
                    ? CourseSelectionState.Racing
                    : CourseSelectionState.BackingOff;
                snapshot = CreateSnapshot(state, reason);
                return true;
            }
        }

        public CourseSelectionSnapshot GetFinalForPublication()
        {
            lock (sync)
            {
                finalSnapshot ??= CreateSnapshot(
                    CourseSelectionState.Cancelled,
                    "已停止");
                if (finalPublished)
                {
                    throw new InvalidOperationException("Final progress was already published.");
                }

                finalPublished = true;
                return finalSnapshot;
            }
        }

        private CourseSelectionSnapshot CreateSnapshot(
            CourseSelectionState state,
            string result) =>
            new(
                Course.CourseId,
                Course.Name,
                state,
                attempts,
                stopwatch.Elapsed,
                result,
                0);
    }

    private sealed class ProgressPublisher(Action<CourseSelectionSnapshot> progress)
    {
        private readonly object sync = new();
        private long version;

        public void Execute(Action action)
        {
            lock (sync)
            {
                action();
            }
        }

        public TResult Execute<TResult>(Func<TResult> action)
        {
            lock (sync)
            {
                return action();
            }
        }

        public bool TryPublish(Func<CourseSelectionSnapshot?> snapshotFactory)
        {
            lock (sync)
            {
                var snapshot = snapshotFactory();
                if (snapshot is null)
                {
                    return false;
                }

                PublishCore(snapshot);
                return true;
            }
        }

        public CourseSelectionSnapshot Publish(
            Func<CourseSelectionSnapshot> snapshotFactory)
        {
            lock (sync)
            {
                return PublishCore(snapshotFactory());
            }
        }

        private CourseSelectionSnapshot PublishCore(CourseSelectionSnapshot snapshot)
        {
            var versioned = snapshot with { Version = ++version };
            progress(versioned);
            return versioned;
        }
    }

    private sealed class FirstFault
    {
        private ExceptionDispatchInfo? captured;

        public bool HasFault => Volatile.Read(ref captured) is not null;

        public void Capture(Exception exception)
        {
            Interlocked.CompareExchange(
                ref captured,
                ExceptionDispatchInfo.Capture(exception),
                null);
        }

        public void ThrowIfCaptured()
        {
            Volatile.Read(ref captured)?.Throw();
        }
    }
}
