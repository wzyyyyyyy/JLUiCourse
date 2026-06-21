using System.Diagnostics;
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

        using var limiter = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        var courseTasks = new Task<CourseSelectionSnapshot>[courses.Count];
        for (var index = 0; index < courses.Count; index++)
        {
            courseTasks[index] = RunCourseAsync(
                courses[index],
                transport,
                limiter,
                progress,
                token);
        }

        return await Task.WhenAll(courseTasks).ConfigureAwait(false);
    }

    private async Task<CourseSelectionSnapshot> RunCourseAsync(
        Course course,
        ICourseSelectionTransport transport,
        SemaphoreSlim limiter,
        Action<CourseSelectionSnapshot> progress,
        CancellationToken runToken)
    {
        using var courseCancellation = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        var runtime = new CourseRuntime(course, progress);
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lanes = new Task[options.LanesPerCourse];

        for (var lane = 0; lane < lanes.Length; lane++)
        {
            lanes[lane] = RunLaneAsync(
                runtime,
                transport,
                limiter,
                startSignal.Task,
                courseCancellation);
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
        finally
        {
            courseCancellation.Cancel();
            await SuppressCancellationAsync(Task.WhenAll(lanes)).ConfigureAwait(false);
        }

        var final = runtime.GetFinalOrCancel();
        runtime.PublishFinal(final);
        return final;
    }

    private async Task RunLaneAsync(
        CourseRuntime runtime,
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
                if (!runtime.TryBeginAttempt())
                {
                    return;
                }

                beganAttempt = true;
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
                    runtime.EndAttempt();
                    beganAttempt = false;
                }

                var classification = classifier.Classify(attempt);
                var outcome = runtime.ProcessClassification(
                    classification,
                    options.UnknownResponseLimit);

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

                if (!runtime.PublishRetry(classification.Reason))
                {
                    return;
                }
            }
            finally
            {
                if (beganAttempt)
                {
                    runtime.EndAttempt();
                }

                limiter.Release();
            }

            await delay.WaitAsync(wait, token).ConfigureAwait(false);
        }
    }

    private static async Task SuppressCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private enum ClassificationOutcome
    {
        Continue,
        Finalized,
        AlreadyFinal
    }

    private sealed class CourseRuntime(
        Course course,
        Action<CourseSelectionSnapshot> progress)
    {
        private readonly object sync = new();
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private int attempts;
        private int inFlight;
        private long version;
        private string? unknownReason;
        private int unknownCount;
        private CourseSelectionSnapshot? finalSnapshot;
        private bool finalPublished;

        public Course Course { get; } = course;

        public bool TryBeginAttempt()
        {
            lock (sync)
            {
                if (finalSnapshot is not null)
                {
                    return false;
                }

                attempts++;
                inFlight++;
                progress(CreateSnapshot(CourseSelectionState.Racing, "正在尝试"));
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

        public bool PublishRetry(string reason)
        {
            lock (sync)
            {
                if (finalSnapshot is not null)
                {
                    return false;
                }

                var state = inFlight > 0
                    ? CourseSelectionState.Racing
                    : CourseSelectionState.BackingOff;
                progress(CreateSnapshot(state, reason));
                return true;
            }
        }

        public CourseSelectionSnapshot GetFinalOrCancel()
        {
            lock (sync)
            {
                finalSnapshot ??= CreateSnapshot(
                    CourseSelectionState.Cancelled,
                    "已停止");
                return finalSnapshot;
            }
        }

        public void PublishFinal(CourseSelectionSnapshot snapshot)
        {
            lock (sync)
            {
                if (finalPublished)
                {
                    return;
                }

                finalPublished = true;
                progress(snapshot);
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
                ++version);
    }
}
