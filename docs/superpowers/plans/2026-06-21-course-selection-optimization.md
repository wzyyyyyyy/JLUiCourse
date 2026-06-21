# Course Selection Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the persistent worker queue with a bounded two-lane racing engine and show stable, independently updated status for every course.

**Architecture:** A pure classifier interprets server responses, a testable engine owns all concurrency for one run, and a per-run HTTP transport creates immutable requests. `JLUiCourseApi` coordinates favorites and cancellation, while typed messages update per-course view models on the UI thread.

**Tech Stack:** .NET 10, C# async/await, `SemaphoreSlim`, `HttpClient`, Newtonsoft.Json, CommunityToolkit.Mvvm, Avalonia 12, xUnit.

---

## File Map

- Create `iCourse/Models/CourseSelectionModels.cs`: selection states, snapshots, attempts, classifications, and options.
- Create `iCourse/Services/CourseSelectionResponseClassifier.cs`: pure HTTP/business response classification.
- Create `iCourse/Services/ICourseSelectionDelay.cs`: retry-delay abstraction and aggressive production implementation.
- Create `iCourse/Services/ICourseSelectionTransport.cs`: one-attempt transport contract and per-run HTTP implementation.
- Create `iCourse/Services/CourseSelectionEngine.cs`: two-lane racing, global concurrency, cancellation, and progress.
- Create `iCourse/Services/CourseSelectionRunGuard.cs`: single-active-run lifecycle.
- Create `iCourse/Messages/CourseSelectionMessages.cs`: typed run/status/banner messages.
- Create `iCourse/ViewModels/CourseSelectionStatusItem.cs`: mutable UI row backed by immutable snapshots.
- Modify `iCourse/Helpers/JLUiCourseApi.cs`: replace persistent queue/workers with engine coordination.
- Modify `iCourse/Services/IJLUiCourseApi.cs`: expose Stop.
- Modify `iCourse/App.axaml.cs` and `iCourse/Services/DesignTimeServices.cs`: register engine dependencies and messenger.
- Modify `iCourse/ViewModels/MainWindowViewModel.cs`: own status rows, counts, banner, and Start/Stop state.
- Modify `iCourse/Views/MainWindow.axaml` and `iCourse/Views/LoginControl.axaml`: status table and run controls.
- Modify `iCourse/Helpers/Logger.cs`: keep diagnostics file-only.
- Delete `iCourse/Messages/SelectCourseFinishedMessage.cs` and `iCourse/Behaviors/AutoScrollToEndBehavior.cs`: remove obsolete log-panel flow.
- Add focused tests under `iCourse.Tests/Models`, `iCourse.Tests/Services`, `iCourse.Tests/ViewModels`, and `iCourse.Tests/Views`.

### Task 1: Domain contracts and response classification

**Files:**
- Create: `iCourse/Models/CourseSelectionModels.cs`
- Create: `iCourse/Services/CourseSelectionResponseClassifier.cs`
- Test: `iCourse.Tests/Services/CourseSelectionResponseClassifierTests.cs`

- [ ] **Step 1: Write failing classifier tests**

```csharp
using iCourse.Models;
using iCourse.Services;
using System.Net;

namespace iCourse.Tests.Services;

public sealed class CourseSelectionResponseClassifierTests
{
    private readonly CourseSelectionResponseClassifier classifier = new();

    [Theory]
    [InlineData("{\"code\":200,\"msg\":\"选课成功\"}", CourseSelectionDecision.Success)]
    [InlineData("{\"code\":500,\"msg\":\"该课程已在选课结果中\"}", CourseSelectionDecision.Success)]
    [InlineData("{\"code\":500,\"msg\":\"课容量已满\"}", CourseSelectionDecision.TerminalFailure)]
    [InlineData("{\"code\":500,\"msg\":\"课程时间冲突\"}", CourseSelectionDecision.TerminalFailure)]
    [InlineData("{\"code\":500,\"msg\":\"选课尚未开始\"}", CourseSelectionDecision.Retry)]
    [InlineData("{\"code\":500,\"msg\":\"请求过于频繁\"}", CourseSelectionDecision.RateLimited)]
    public void Classify_MapsKnownBusinessResponses(string body, CourseSelectionDecision expected)
    {
        var result = classifier.Classify(new CourseSelectionAttempt(HttpStatusCode.OK, body));
        Assert.Equal(expected, result.Decision);
    }

    [Fact]
    public void Classify_UsesRetryAfterForHttp429()
    {
        var retryAfter = TimeSpan.FromMilliseconds(750);
        var result = classifier.Classify(new CourseSelectionAttempt(HttpStatusCode.TooManyRequests, "", retryAfter));
        Assert.Equal(CourseSelectionDecision.RateLimited, result.Decision);
        Assert.Equal(retryAfter, result.RetryAfter);
    }

    [Fact]
    public void Classify_MarksUnknownResponseForBoundedRetry()
    {
        var result = classifier.Classify(new CourseSelectionAttempt(HttpStatusCode.OK, "{\"code\":500,\"msg\":\"新返回值\"}"));
        Assert.Equal(CourseSelectionDecision.Retry, result.Decision);
        Assert.True(result.IsUnknown);
        Assert.Equal("新返回值", result.Reason);
    }
}
```

- [ ] **Step 2: Run the classifier tests and verify RED**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter CourseSelectionResponseClassifierTests`

Expected: FAIL because `CourseSelectionResponseClassifier`, `CourseSelectionAttempt`, and related domain types do not exist.

- [ ] **Step 3: Add the selection domain contracts**

Create `iCourse/Models/CourseSelectionModels.cs`:

```csharp
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
        new(course.CourseId, course.Name, CourseSelectionState.Waiting, 0, TimeSpan.Zero, "等待开始", 0);
}

public sealed record CourseSelectionOptions(
    int LanesPerCourse = 2,
    int MaxConcurrency = 20,
    int UnknownResponseLimit = 5);
```

- [ ] **Step 4: Implement the pure response classifier**

Create `iCourse/Services/CourseSelectionResponseClassifier.cs`:

```csharp
using iCourse.Models;
using Newtonsoft.Json.Linq;
using System.Net;

namespace iCourse.Services;

public sealed class CourseSelectionResponseClassifier
{
    private static readonly string[] PermanentFailureFragments =
    [
        "课容量已满", "时间冲突", "不符合", "不可选", "无效", "批次已结束", "资格", "学分上限"
    ];

    private static readonly string[] TransientFragments =
    [
        "尚未开始", "未到选课时间", "系统繁忙", "系统异常", "处理中"
    ];

    private static readonly string[] RateLimitFragments =
    [
        "请求过于频繁", "操作频繁", "稍后再试"
    ];

    public CourseSelectionClassification Classify(CourseSelectionAttempt attempt)
    {
        if (!string.IsNullOrWhiteSpace(attempt.Error))
        {
            return new(CourseSelectionDecision.Retry, attempt.Error);
        }

        if (attempt.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new(CourseSelectionDecision.RateLimited, "请求受限", attempt.RetryAfter);
        }

        if (attempt.StatusCode is HttpStatusCode.RequestTimeout || (int?)attempt.StatusCode >= 500)
        {
            return new(CourseSelectionDecision.Retry, $"服务器暂时不可用 ({(int?)attempt.StatusCode})");
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
            var text = string.IsNullOrWhiteSpace(attempt.Body) ? "空响应" : attempt.Body.Trim();
            return new(CourseSelectionDecision.Retry, text, IsUnknown: true);
        }

        if (code == 200 || message.Contains("已在选课结果中", StringComparison.Ordinal))
        {
            return new(CourseSelectionDecision.Success, message);
        }

        if (RateLimitFragments.Any(message.Contains))
        {
            return new(CourseSelectionDecision.RateLimited, message, attempt.RetryAfter);
        }

        if (PermanentFailureFragments.Any(message.Contains))
        {
            return new(CourseSelectionDecision.TerminalFailure, message);
        }

        if (TransientFragments.Any(message.Contains))
        {
            return new(CourseSelectionDecision.Retry, message);
        }

        return new(CourseSelectionDecision.Retry, message, IsUnknown: true);
    }
}
```

- [ ] **Step 5: Run classifier tests and verify GREEN**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter CourseSelectionResponseClassifierTests`

Expected: PASS, 8 test cases.

- [ ] **Step 6: Commit classifier work**

```bash
git add iCourse/Models/CourseSelectionModels.cs iCourse/Services/CourseSelectionResponseClassifier.cs iCourse.Tests/Services/CourseSelectionResponseClassifierTests.cs
git commit -m "feat: classify course selection responses"
```

### Task 2: Two-lane bounded racing engine

**Files:**
- Create: `iCourse/Services/ICourseSelectionDelay.cs`
- Create: `iCourse/Services/ICourseSelectionTransport.cs`
- Create: `iCourse/Services/CourseSelectionEngine.cs`
- Test: `iCourse.Tests/Services/CourseSelectionEngineTests.cs`

- [ ] **Step 1: Write failing racing and cancellation tests**

Create `iCourse.Tests/Services/CourseSelectionEngineTests.cs` with a scripted transport that tracks active calls:

```csharp
using iCourse.Models;
using iCourse.Services;
using System.Collections.Concurrent;
using System.Net;

namespace iCourse.Tests.Services;

public sealed class CourseSelectionEngineTests
{
    [Fact]
    public async Task RunAsync_StartsTwoLanesAndCancelsSiblingOnSuccess()
    {
        var secondLaneCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var transport = new ScriptedTransport(async (_, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 2) bothStarted.TrySetResult();
            await bothStarted.Task.WaitAsync(token);
            if (call == 1)
            {
                return new(HttpStatusCode.OK, "{\"code\":200,\"msg\":\"选课成功\"}");
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("unreachable");
            }
            catch (OperationCanceledException)
            {
                secondLaneCancelled.TrySetResult();
                throw;
            }
        });

        var engine = CreateEngine();
        var result = await engine.RunAsync([Course("1")], transport, _ => { }, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(CourseSelectionState.Succeeded, Assert.Single(result).State);
        await secondLaneCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunAsync_TreatsCapacityFullAsImmediateFailure()
    {
        var transport = new ScriptedTransport((_, _) => Task.FromResult(
            new CourseSelectionAttempt(HttpStatusCode.OK, "{\"code\":500,\"msg\":\"课容量已满\"}")));

        var result = await CreateEngine().RunAsync([Course("1")], transport, _ => { }, CancellationToken.None);

        Assert.Equal(CourseSelectionState.Failed, Assert.Single(result).State);
        Assert.Equal("课容量已满", result[0].LatestResult);
    }

    private static CourseSelectionEngine CreateEngine(int maxConcurrency = 20) =>
        new(new CourseSelectionResponseClassifier(), new ImmediateDelay(), new(2, maxConcurrency, 5));

    private static Course Course(string id) => new() { CourseId = id, Name = $"课程{id}" };

    private sealed class ImmediateDelay : ICourseSelectionDelay
    {
        public TimeSpan GetTransientDelay() => TimeSpan.Zero;
        public TimeSpan GetNetworkDelay(int failureCount) => TimeSpan.Zero;
        public TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount) => retryAfter ?? TimeSpan.Zero;
        public Task WaitAsync(TimeSpan delay, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class ScriptedTransport(Func<Course, CancellationToken, Task<CourseSelectionAttempt>> handler)
        : ICourseSelectionTransport
    {
        public Task<CourseSelectionAttempt> SendAsync(Course course, CancellationToken token) => handler(course, token);
    }
}
```

- [ ] **Step 2: Run racing tests and verify RED**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter CourseSelectionEngineTests`

Expected: FAIL because the engine, transport, and delay contracts do not exist.

- [ ] **Step 3: Add transport and delay contracts**

Create `iCourse/Services/ICourseSelectionTransport.cs`:

```csharp
using iCourse.Models;

namespace iCourse.Services;

public interface ICourseSelectionTransport
{
    Task<CourseSelectionAttempt> SendAsync(Course course, CancellationToken token);
}
```

Create `iCourse/Services/ICourseSelectionDelay.cs`:

```csharp
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
    public TimeSpan GetTransientDelay() => TimeSpan.FromMilliseconds(Random.Shared.Next(40, 101));

    public TimeSpan GetNetworkDelay(int failureCount) =>
        TimeSpan.FromMilliseconds(Math.Min(1000, 100 * Math.Pow(2, Math.Min(failureCount - 1, 4))));

    public TimeSpan GetRateLimitDelay(TimeSpan? retryAfter, int failureCount) => retryAfter ??
        TimeSpan.FromMilliseconds(Math.Min(2000, 250 * Math.Pow(2, Math.Min(failureCount - 1, 3))));

    public Task WaitAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);
}
```

- [ ] **Step 4: Implement the racing engine**

Create `iCourse/Services/CourseSelectionEngine.cs`. Use one local `SemaphoreSlim` per run, start exactly `LanesPerCourse` tasks per course, and keep mutable counters inside the nested `CourseRuntime` lock. The public surface and terminal flow must be:

```csharp
using iCourse.Models;
using System.Diagnostics;

namespace iCourse.Services;

public sealed class CourseSelectionEngine(
    CourseSelectionResponseClassifier classifier,
    ICourseSelectionDelay delay,
    CourseSelectionOptions options)
{
    public async Task<IReadOnlyList<CourseSelectionSnapshot>> RunAsync(
        IReadOnlyList<Course> courses,
        ICourseSelectionTransport transport,
        Action<CourseSelectionSnapshot> progress,
        CancellationToken token)
    {
        using var limiter = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        var tasks = courses.Select(course => RunCourseAsync(course, transport, limiter, progress, token));
        return await Task.WhenAll(tasks);
    }

    private async Task<CourseSelectionSnapshot> RunCourseAsync(
        Course course,
        ICourseSelectionTransport transport,
        SemaphoreSlim limiter,
        Action<CourseSelectionSnapshot> progress,
        CancellationToken runToken)
    {
        using var courseCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        var runtime = new CourseRuntime(course);
        var lanes = Enumerable.Range(0, options.LanesPerCourse)
            .Select(_ => RunLaneAsync(runtime, transport, limiter, progress, courseCts.Token))
            .ToArray();

        try
        {
            var winnerTask = await Task.WhenAny(lanes);
            var winner = await winnerTask;
            courseCts.Cancel();
            await SuppressCancellationAsync(Task.WhenAll(lanes));
            progress(winner);
            return winner;
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            courseCts.Cancel();
            await SuppressCancellationAsync(Task.WhenAll(lanes));
            var cancelled = runtime.Snapshot(CourseSelectionState.Cancelled, "已停止");
            progress(cancelled);
            return cancelled;
        }
    }

    private async Task<CourseSelectionSnapshot> RunLaneAsync(
        CourseRuntime runtime,
        ICourseSelectionTransport transport,
        SemaphoreSlim limiter,
        Action<CourseSelectionSnapshot> progress,
        CancellationToken token)
    {
        var retryCount = 0;
        while (true)
        {
            await limiter.WaitAsync(token);
            var racing = runtime.BeginAttempt();
            progress(racing);

            CourseSelectionAttempt attempt;
            try
            {
                attempt = await transport.SendAsync(runtime.Course, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                attempt = new(null, "", Error: ex.Message);
            }
            finally
            {
                runtime.EndAttempt();
                limiter.Release();
            }

            var classification = classifier.Classify(attempt);
            if (classification.IsUnknown)
            {
                if (runtime.RecordUnknown(classification.Reason) >= options.UnknownResponseLimit)
                {
                    return runtime.Snapshot(CourseSelectionState.Failed, classification.Reason);
                }
            }
            else
            {
                runtime.ClearUnknown();
            }

            if (classification.Decision == CourseSelectionDecision.Success)
            {
                return runtime.Snapshot(CourseSelectionState.Succeeded, classification.Reason);
            }

            if (classification.Decision == CourseSelectionDecision.TerminalFailure)
            {
                return runtime.Snapshot(CourseSelectionState.Failed, classification.Reason);
            }

            retryCount++;
            var wait = classification.Decision == CourseSelectionDecision.RateLimited
                ? delay.GetRateLimitDelay(classification.RetryAfter, retryCount)
                : attempt.Error is null
                    ? delay.GetTransientDelay()
                    : delay.GetNetworkDelay(retryCount);
            progress(runtime.Snapshot(runtime.HasInFlight ? CourseSelectionState.Racing : CourseSelectionState.BackingOff, classification.Reason));
            await delay.WaitAsync(wait, token);
        }
    }

    private static async Task SuppressCancellationAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }

    private sealed class CourseRuntime(Course course)
    {
        private readonly object sync = new();
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private int attempts;
        private int inFlight;
        private long version;
        private string? unknownReason;
        private int unknownCount;

        public Course Course { get; } = course;

        public bool HasInFlight
        {
            get { lock (sync) return inFlight > 0; }
        }

        public CourseSelectionSnapshot BeginAttempt()
        {
            lock (sync)
            {
                attempts++;
                inFlight++;
                return Create(CourseSelectionState.Racing, "正在尝试");
            }
        }

        public void EndAttempt()
        {
            lock (sync) inFlight--;
        }

        public int RecordUnknown(string reason)
        {
            lock (sync)
            {
                unknownCount = unknownReason == reason ? unknownCount + 1 : 1;
                unknownReason = reason;
                return unknownCount;
            }
        }

        public void ClearUnknown()
        {
            lock (sync)
            {
                unknownReason = null;
                unknownCount = 0;
            }
        }

        public CourseSelectionSnapshot Snapshot(CourseSelectionState state, string result)
        {
            lock (sync) return Create(state, result);
        }

        private CourseSelectionSnapshot Create(CourseSelectionState state, string result) =>
            new(Course.CourseId, Course.Name, state, attempts, stopwatch.Elapsed, result, ++version);
    }
}
```

- [ ] **Step 5: Run racing tests and verify GREEN**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter CourseSelectionEngineTests`

Expected: PASS for two-lane start, sibling cancellation, and immediate capacity-full behavior.

- [ ] **Step 6: Add failing global-limit, unknown-response, and run-cancellation tests**

```csharp
[Fact]
public async Task RunAsync_NeverExceedsGlobalConcurrencyLimit()
{
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var reachedLimit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var active = 0;
    var maxActive = 0;
    var transport = new ScriptedTransport(async (_, token) =>
    {
        var current = Interlocked.Increment(ref active);
        UpdateMax(ref maxActive, current);
        if (current == 20) reachedLimit.TrySetResult();
        try { await release.Task.WaitAsync(token); }
        finally { Interlocked.Decrement(ref active); }
        return new(HttpStatusCode.OK, "{\"code\":500,\"msg\":\"课容量已满\"}");
    });

    var run = CreateEngine().RunAsync(
        Enumerable.Range(1, 50).Select(i => Course(i.ToString())).ToList(),
        transport,
        _ => { },
        CancellationToken.None);

    await reachedLimit.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(20, maxActive);
    release.TrySetResult();
    await run;
}

private static void UpdateMax(ref int target, int value)
{
    int observed;
    do
    {
        observed = target;
        if (observed >= value) return;
    } while (Interlocked.CompareExchange(ref target, value, observed) != observed);
}

[Fact]
public async Task RunAsync_StopsAfterRepeatedUnknownResponse()
{
    var transport = new ScriptedTransport((_, _) => Task.FromResult(
        new CourseSelectionAttempt(HttpStatusCode.OK, "{\"code\":500,\"msg\":\"新返回值\"}")));

    var result = await CreateEngine().RunAsync([Course("1")], transport, _ => { }, CancellationToken.None);

    var final = Assert.Single(result);
    Assert.Equal(CourseSelectionState.Failed, final.State);
    Assert.Equal("新返回值", final.LatestResult);
    Assert.InRange(final.AttemptCount, 5, 6);
}

[Fact]
public async Task RunAsync_CancellationStopsEveryActiveLane()
{
    using var cancellation = new CancellationTokenSource();
    var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var active = 0;
    var transport = new ScriptedTransport(async (_, token) =>
    {
        if (Interlocked.Increment(ref active) == 2) bothStarted.TrySetResult();
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

    var run = CreateEngine().RunAsync([Course("1")], transport, _ => { }, cancellation.Token);
    await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    cancellation.Cancel();
    var result = await run;

    Assert.Equal(CourseSelectionState.Cancelled, Assert.Single(result).State);
    Assert.Equal(0, active);
}
```

- [ ] **Step 7: Run the expanded engine suite and fix only engine defects**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter CourseSelectionEngineTests`

Expected: PASS; maximum active calls is exactly 20, unknown responses terminate, and cancellation leaves no active transport call.

- [ ] **Step 8: Commit engine work**

```bash
git add iCourse/Services/ICourseSelectionDelay.cs iCourse/Services/ICourseSelectionTransport.cs iCourse/Services/CourseSelectionEngine.cs iCourse.Tests/Services/CourseSelectionEngineTests.cs
git commit -m "feat: add bounded course selection racing engine"
```

### Task 3: HTTP transport and API run coordination

**Files:**
- Modify: `iCourse/Services/ICourseSelectionTransport.cs`
- Create: `iCourse/Services/CourseSelectionRunGuard.cs`
- Create: `iCourse/Messages/CourseSelectionMessages.cs`
- Modify: `iCourse/Helpers/JLUiCourseApi.cs:17-33,133-163,300-379`
- Modify: `iCourse/Services/IJLUiCourseApi.cs:7-15`
- Modify: `iCourse/App.axaml.cs:47-61`
- Modify: `iCourse/Services/DesignTimeServices.cs:9-23`
- Test: `iCourse.Tests/Services/CourseSelectionHttpTransportTests.cs`
- Test: `iCourse.Tests/Services/CourseSelectionRunGuardTests.cs`

- [ ] **Step 1: Write failing transport and run-guard tests**

```csharp
[Fact]
public async Task SendAsync_CreatesFreshFormRequestWithPerRequestReferrer()
{
    var handler = new CapturingHandler();
    using var client = new HttpClient(handler) { BaseAddress = new("https://icourses.jlu.edu.cn") };
    var transport = new CourseSelectionHttpTransport(client, "batch-1");
    var course = new Course { CourseId = "course-1", SecretVal = "secret", SelectType = ClassSelectType.Elective };

    await transport.SendAsync(course, CancellationToken.None);

    Assert.Equal("https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId=batch-1", handler.Referrer);
    Assert.Contains("clazzId=course-1", handler.Body);
    Assert.Contains("secretVal=secret", handler.Body);
}

[Fact]
public void RunGuard_RejectsSecondRunAndCancelsActiveRun()
{
    var guard = new CourseSelectionRunGuard();
    Assert.True(guard.TryBegin(out var token));
    Assert.False(guard.TryBegin(out _));
    guard.Cancel();
    Assert.True(token.IsCancellationRequested);
    guard.Complete();
    Assert.True(guard.TryBegin(out _));
    guard.Complete();
}
```

Include this handler in `CourseSelectionHttpTransportTests.cs`:

```csharp
private sealed class CapturingHandler : HttpMessageHandler
{
    public string? Referrer { get; private set; }
    public string Body { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Referrer = request.Headers.Referrer?.ToString();
        Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(token);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"code\":200,\"msg\":\"选课成功\"}")
        };
    }
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter "CourseSelectionHttpTransportTests|CourseSelectionRunGuardTests"`

Expected: FAIL because the production transport and guard do not exist.

- [ ] **Step 3: Implement the per-run HTTP transport**

Replace `iCourse/Services/ICourseSelectionTransport.cs` with:

```csharp
using iCourse.Models;

namespace iCourse.Services;

public interface ICourseSelectionTransport
{
    Task<CourseSelectionAttempt> SendAsync(Course course, CancellationToken token);
}

public sealed class CourseSelectionHttpTransport(HttpClient client, string batchId) : ICourseSelectionTransport
{
    public async Task<CourseSelectionAttempt> SendAsync(Course course, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "xsxk/sc/clazz/addxk");
        request.Headers.Referrer = new Uri($"https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId={batchId}");
        request.Headers.TryAddWithoutValidation("Origin", "https://icourses.jlu.edu.cn");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["clazzId"] = course.CourseId,
            ["secretVal"] = course.SecretVal,
            ["clazzType"] = course.SelectType.ToString()
        });

        using var response = await client.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is DateTimeOffset retryDate)
        {
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        }

        return new(response.StatusCode, body, retryAfter);
    }
}
```

- [ ] **Step 4: Implement the single-run guard**

Create `iCourse/Services/CourseSelectionRunGuard.cs`:

```csharp
namespace iCourse.Services;

public sealed class CourseSelectionRunGuard
{
    private readonly object sync = new();
    private CancellationTokenSource? active;

    public bool TryBegin(out CancellationToken token)
    {
        lock (sync)
        {
            if (active is not null)
            {
                token = default;
                return false;
            }

            active = new CancellationTokenSource();
            token = active.Token;
            return true;
        }
    }

    public void Cancel()
    {
        lock (sync) active?.Cancel();
    }

    public void Complete()
    {
        lock (sync)
        {
            active?.Dispose();
            active = null;
        }
    }
}
```

- [ ] **Step 5: Run transport and guard tests and verify GREEN**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter "CourseSelectionHttpTransportTests|CourseSelectionRunGuardTests"`

Expected: PASS.

- [ ] **Step 6: Add typed selection messages**

Create `iCourse/Messages/CourseSelectionMessages.cs`:

```csharp
using iCourse.Models;

namespace iCourse.Messages;

public sealed record CourseSelectionRunStartedMessage(IReadOnlyList<CourseSelectionSnapshot> Courses);
public sealed record CourseSelectionStatusChangedMessage(CourseSelectionSnapshot Status);
public sealed record CourseSelectionRunCompletedMessage(bool WasCancelled);
public enum SystemBannerSeverity { Info, Warning, Error }
public sealed record SystemBannerMessage(string Text, SystemBannerSeverity Severity = SystemBannerSeverity.Info);
```

- [ ] **Step 7: Replace the queue/worker path in `JLUiCourseApi`**

Change the primary constructor and state to:

```csharp
public class JLUiCourseApi(
    Logger logger,
    UserCredentials credentials,
    IDialogService dialogs,
    IAppLifetime lifetime,
    CourseSelectionEngine selectionEngine,
    IMessenger messenger) : IJLUiCourseApi
{
    private readonly CourseSelectionRunGuard selectionRunGuard = new();
```

Remove `cts`, `Random`, `taskQueue`, `workerCount`, `BuildRequestBody`, `StartWorkerPool`, `TrySelectCourseWithBackoffAsync`, and `EnqueueSelectCourseAsync`, then replace `StartSelectClassAsync` with:

```csharp
public async Task StartSelectClassAsync()
{
    if (!selectionRunGuard.TryBegin(out var token))
    {
        messenger.Send(new SystemBannerMessage("选课任务正在进行中", SystemBannerSeverity.Warning));
        return;
    }

    var cancelled = false;
    try
    {
        var courses = await GetFavoriteCoursesAsync();
        if (courses.Count == 0)
        {
            messenger.Send(new SystemBannerMessage("收藏列表为空，请先添加课程", SystemBannerSeverity.Warning));
            return;
        }

        var waiting = courses.Select(CourseSelectionSnapshot.Waiting).ToList();
        messenger.Send(new CourseSelectionRunStartedMessage(waiting));
        var transport = new CourseSelectionHttpTransport(client, batch.batchId);
        var results = await selectionEngine.RunAsync(
            courses,
            transport,
            status => messenger.Send(new CourseSelectionStatusChangedMessage(status)),
            token);

        cancelled = results.Any(result => result.State == CourseSelectionState.Cancelled);
        foreach (var result in results)
        {
            logger.WriteLine($"课程结果: {result.CourseName}, {result.State}, 尝试 {result.AttemptCount} 次, {result.LatestResult}");
        }

        logger.WriteLine($"选课结束: 成功 {results.Count(r => r.State == CourseSelectionState.Succeeded)}, 失败 {results.Count(r => r.State == CourseSelectionState.Failed)}");
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }
    catch (Exception ex)
    {
        logger.WriteLine($"选课任务异常: {ex}");
        messenger.Send(new SystemBannerMessage("选课任务异常，请查看日志", SystemBannerSeverity.Error));
    }
    finally
    {
        selectionRunGuard.Complete();
        messenger.Send(new CourseSelectionRunCompletedMessage(cancelled));
    }
}

public void StopSelectClass() => selectionRunGuard.Cancel();
```

Change `IJLUiCourseApi` to include `void StopSelectClass();`. Replace static `WeakReferenceMessenger.Default.Send` calls in the API with the injected `IMessenger`.

Add concise system banners at the existing failure branches:

```csharp
// AttemptLoginAsync: before retrying an invalid captcha.
messenger.Send(new SystemBannerMessage("验证码错误，请重新输入", SystemBannerSeverity.Warning));

// AttemptLoginAsync: alongside the final non-success log.
messenger.Send(new SystemBannerMessage($"登录失败：{msg}", SystemBannerSeverity.Error));

// SetBatchIdAsync: alongside a non-200 response.
messenger.Send(new SystemBannerMessage($"批次设置失败：{json["msg"]}", SystemBannerSeverity.Error));

// KeepOnline: before displaying the restart dialog.
messenger.Send(new SystemBannerMessage("登录状态已失效，应用即将重启", SystemBannerSeverity.Error));
```

- [ ] **Step 8: Register dependencies**

In both `App.ConfigureServices` and `DesignTimeServices.Create`, add:

```csharp
services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
services.AddSingleton(new CourseSelectionOptions());
services.AddSingleton<CourseSelectionResponseClassifier>();
services.AddSingleton<ICourseSelectionDelay, AggressiveCourseSelectionDelay>();
services.AddSingleton<CourseSelectionEngine>();
```

- [ ] **Step 9: Run all service tests and build**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter "CourseSelection"`

Expected: PASS.

Run: `dotnet build iCourse/iCourse.csproj --configuration Release`

Expected: build succeeds after every `IJLUiCourseApi` fake adds `StopSelectClass()`.

- [ ] **Step 10: Commit transport and coordination**

```bash
git add iCourse/Services/ICourseSelectionTransport.cs iCourse/Services/CourseSelectionRunGuard.cs iCourse/Messages/CourseSelectionMessages.cs iCourse/Helpers/JLUiCourseApi.cs iCourse/Services/IJLUiCourseApi.cs iCourse/App.axaml.cs iCourse/Services/DesignTimeServices.cs iCourse.Tests/Services/CourseSelectionHttpTransportTests.cs iCourse.Tests/Services/CourseSelectionRunGuardTests.cs
git commit -m "refactor: coordinate bounded selection runs"
```

### Task 4: Per-course status view model and messages

**Files:**
- Create: `iCourse/ViewModels/CourseSelectionStatusItem.cs`
- Modify: `iCourse/ViewModels/MainWindowViewModel.cs:14-136`
- Modify: `iCourse.Tests/ViewModels/MainWindowViewModelTests.cs:9-46`

- [ ] **Step 1: Write failing status-row tests**

Add a local `WeakReferenceMessenger`, pass it and `ImmediateUiDispatcher` to the view model, then add:

```csharp
[Fact]
public void StatusMessages_UpdateExistingCourseRowAndSummary()
{
    var (viewModel, messenger, _) = CreateViewModel();
    var waiting = new CourseSelectionSnapshot("c1", "高等数学", CourseSelectionState.Waiting, 0, TimeSpan.Zero, "等待开始", 0);
    messenger.Send(new CourseSelectionRunStartedMessage([waiting]));
    messenger.Send(new CourseSelectionStatusChangedMessage(
        waiting with { State = CourseSelectionState.Racing, AttemptCount = 2, LatestResult = "正在尝试", Version = 1 }));
    messenger.Send(new CourseSelectionStatusChangedMessage(
        waiting with { State = CourseSelectionState.Succeeded, AttemptCount = 3, LatestResult = "选课成功", Version = 2 }));

    var row = Assert.Single(viewModel.CourseStatuses);
    Assert.Equal("成功", row.StateText);
    Assert.Equal(3, row.AttemptCount);
    Assert.Equal(1, viewModel.SucceededCount);
    Assert.Equal(100, viewModel.ProgressValue);
}

[Fact]
public async Task StopCommand_CancelsApiWithoutStartingSecondRun()
{
    var (viewModel, _, api) = CreateViewModel();
    await viewModel.StartSelectCourseCommand.ExecuteAsync(null);
    viewModel.StopSelectCourseCommand.Execute(null);
    Assert.Equal(1, api.StartCalls);
    Assert.Equal(1, api.StopCalls);
}
```

The test factory must create isolated credentials, `FakeApi`, `ImmediateUiDispatcher`, and `WeakReferenceMessenger` instances so tests cannot receive one another's messages.

- [ ] **Step 2: Run view-model tests and verify RED**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter MainWindowViewModelTests`

Expected: FAIL because status rows, summary properties, message handlers, and Stop command do not exist.

- [ ] **Step 3: Implement the row view model**

Create `iCourse/ViewModels/CourseSelectionStatusItem.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using iCourse.Models;

namespace iCourse.ViewModels;

public partial class CourseSelectionStatusItem : ObservableObject
{
    public CourseSelectionStatusItem(CourseSelectionSnapshot snapshot)
    {
        CourseId = snapshot.CourseId;
        CourseName = snapshot.CourseName;
        Apply(snapshot);
    }

    public string CourseId { get; }
    public string CourseName { get; }

    [ObservableProperty] private CourseSelectionState state;
    [ObservableProperty] private int attemptCount;
    [ObservableProperty] private string elapsedText = "0.0 秒";
    [ObservableProperty] private string latestResult = string.Empty;
    [ObservableProperty] private long version;

    public string StateText => State switch
    {
        CourseSelectionState.Waiting => "等待",
        CourseSelectionState.Racing => "竞速中",
        CourseSelectionState.BackingOff => "退避中",
        CourseSelectionState.Succeeded => "成功",
        CourseSelectionState.Failed => "失败",
        CourseSelectionState.Cancelled => "已停止",
        _ => "未知"
    };

    public bool IsWaiting => State is CourseSelectionState.Waiting or CourseSelectionState.Cancelled;
    public bool IsRacing => State == CourseSelectionState.Racing;
    public bool IsBackingOff => State == CourseSelectionState.BackingOff;
    public bool IsSucceeded => State == CourseSelectionState.Succeeded;
    public bool IsFailed => State == CourseSelectionState.Failed;

    public void Apply(CourseSelectionSnapshot snapshot)
    {
        if (snapshot.Version < Version || State is CourseSelectionState.Succeeded or CourseSelectionState.Failed or CourseSelectionState.Cancelled)
        {
            return;
        }

        Version = snapshot.Version;
        State = snapshot.State;
        AttemptCount = snapshot.AttemptCount;
        ElapsedText = $"{snapshot.Elapsed.TotalSeconds:F1} 秒";
        LatestResult = snapshot.LatestResult;
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsRacing));
        OnPropertyChanged(nameof(IsBackingOff));
        OnPropertyChanged(nameof(IsSucceeded));
        OnPropertyChanged(nameof(IsFailed));
    }
}
```

- [ ] **Step 4: Replace log-panel state in `MainWindowViewModel`**

Inject `IUiDispatcher` and `IMessenger`; remove `LogMessages`; and add the following state and registrations:

```csharp
private readonly IUiDispatcher dispatcher;
private readonly IMessenger messenger;
private readonly Dictionary<string, CourseSelectionStatusItem> rowsByCourseId = new();

public ObservableCollection<CourseSelectionStatusItem> CourseStatuses { get; } = new();

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CanStartSelection))]
private bool isSelectionRunning;

[ObservableProperty] private int totalCount;
[ObservableProperty] private int runningCount;
[ObservableProperty] private int succeededCount;
[ObservableProperty] private int failedCount;
[ObservableProperty] private string bannerText = string.Empty;
[ObservableProperty] private bool isBannerVisible;

public bool CanStartSelection => AreAfterLoginButtonsVisible && !IsSelectionRunning;

// In the constructor after assigning dependencies:
messenger.Register<CourseSelectionRunStartedMessage>(this,
    (_, message) => dispatcher.Post(() => ApplyRunStarted(message)));
messenger.Register<CourseSelectionStatusChangedMessage>(this,
    (_, message) => dispatcher.Post(() => ApplyStatus(message)));
messenger.Register<CourseSelectionRunCompletedMessage>(this,
    (_, message) => dispatcher.Post(() => ApplyRunCompleted(message)));
messenger.Register<SystemBannerMessage>(this,
    (_, message) => dispatcher.Post(() => ApplyBanner(message)));

private void ApplyRunCompleted(CourseSelectionRunCompletedMessage message)
{
    IsSelectionRunning = false;
    if (message.WasCancelled)
    {
        BannerText = "选课任务已停止";
        IsBannerVisible = true;
    }
}

private void ApplyBanner(SystemBannerMessage message)
{
    BannerText = message.Text;
    IsBannerVisible = !string.IsNullOrWhiteSpace(message.Text);
}
```

Add `[NotifyPropertyChangedFor(nameof(CanStartSelection))]` to the existing `areAfterLoginButtonsVisible` observable field. Continue registering `LoginSuccessMessage` through the injected messenger rather than `WeakReferenceMessenger.Default`.

The update and count logic must be exactly:

```csharp
private void ApplyRunStarted(CourseSelectionRunStartedMessage message)
{
    rowsByCourseId.Clear();
    CourseStatuses.Clear();
    foreach (var snapshot in message.Courses)
    {
        var row = new CourseSelectionStatusItem(snapshot);
        rowsByCourseId.Add(snapshot.CourseId, row);
        CourseStatuses.Add(row);
    }

    IsSelectionRunning = true;
    IsProgressVisible = CourseStatuses.Count > 0;
    BannerText = string.Empty;
    IsBannerVisible = false;
    RecalculateSummary();
}

private void ApplyStatus(CourseSelectionStatusChangedMessage message)
{
    if (rowsByCourseId.TryGetValue(message.Status.CourseId, out var row))
    {
        row.Apply(message.Status);
        RecalculateSummary();
    }
}

private void RecalculateSummary()
{
    TotalCount = CourseStatuses.Count;
    RunningCount = CourseStatuses.Count(row => row.State is CourseSelectionState.Waiting or CourseSelectionState.Racing or CourseSelectionState.BackingOff);
    SucceededCount = CourseStatuses.Count(row => row.State == CourseSelectionState.Succeeded);
    FailedCount = CourseStatuses.Count(row => row.State == CourseSelectionState.Failed);
    var finalCount = CourseStatuses.Count(row => row.State is CourseSelectionState.Succeeded or CourseSelectionState.Failed or CourseSelectionState.Cancelled);
    ProgressValue = TotalCount == 0 ? 0 : (double)finalCount / TotalCount * 100;
}

[RelayCommand]
private async Task StartSelectCourse()
{
    if (IsSelectionRunning) return;
    IsSelectionRunning = true;
    try { await api.StartSelectClassAsync(); }
    finally { IsSelectionRunning = false; }
}

[RelayCommand]
private void StopSelectCourse() => api.StopSelectClass();
```

All messenger handlers must wrap these methods with `dispatcher.Post(...)`. `CanStartSelection` must evaluate to `AreAfterLoginButtonsVisible && !IsSelectionRunning` and raise property changes when either source property changes.

Use this isolated fake and factory in `MainWindowViewModelTests`:

```csharp
private static (MainWindowViewModel ViewModel, WeakReferenceMessenger Messenger, FakeApi Api) CreateViewModel()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "Logs"));
    var credentials = new UserCredentials(new FakeAppPaths(root));
    var messenger = new WeakReferenceMessenger();
    var api = new FakeApi();
    var viewModel = new MainWindowViewModel(
        api,
        credentials,
        new FakeDialogService(),
        new ImmediateUiDispatcher(),
        messenger);
    return (viewModel, messenger, api);
}

private sealed class FakeApi : IJLUiCourseApi
{
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public Task AddToFavoritesAsync(Course course) => Task.CompletedTask;
    public Task LoginAsync(string username, string password) => Task.CompletedTask;
    public Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize) => Task.FromResult(new List<Course>());
    public Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize, string key) => Task.FromResult(new List<Course>());
    public Task SetBatchIdAsync(BatchInfo batch) => Task.CompletedTask;
    public Task StartSelectClassAsync() { StartCalls++; return Task.CompletedTask; }
    public void StopSelectClass() => StopCalls++;
}
```

- [ ] **Step 5: Run view-model tests and verify GREEN**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter MainWindowViewModelTests`

Expected: PASS; one course remains one row, summary is correct, and Start/Stop calls are bounded.

- [ ] **Step 6: Commit status view-model work**

```bash
git add iCourse/ViewModels/CourseSelectionStatusItem.cs iCourse/ViewModels/MainWindowViewModel.cs iCourse.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat: track per-course selection status"
```

### Task 5: Status table UI and file-only diagnostics

**Files:**
- Modify: `iCourse/Views/MainWindow.axaml:1-41`
- Modify: `iCourse/Views/LoginControl.axaml:20-29`
- Modify: `iCourse/Helpers/Logger.cs:1-40`
- Modify: `iCourse.Tests/Helpers/LoggerTests.cs:7-25`
- Create: `iCourse.Tests/Views/MainWindowLayoutTests.cs`
- Delete: `iCourse/Messages/SelectCourseFinishedMessage.cs`
- Delete: `iCourse/Behaviors/AutoScrollToEndBehavior.cs`

- [ ] **Step 1: Write failing layout and file-logging tests**

The layout contract test reads `iCourse/Views/MainWindow.axaml` from the repository and asserts it binds `CourseStatuses`, `BannerText`, and summary counts while no longer binding `LogMessages`.

```csharp
[Fact]
public void MainWindow_UsesPerCourseStatusTableInsteadOfLogStream()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    var xaml = File.ReadAllText(Path.Combine(root, "iCourse", "Views", "MainWindow.axaml"));
    Assert.Contains("ItemsSource=\"{Binding CourseStatuses}\"", xaml);
    Assert.Contains("{Binding BannerText}", xaml);
    Assert.Contains("{Binding SucceededCount}", xaml);
    Assert.DoesNotContain("LogMessages", xaml);
}
```

Change `LoggerTests` to dispose the logger and assert `hello` exists in a generated `log*.txt` file rather than in an observable UI collection:

```csharp
[Fact]
public void WriteLine_WritesTimestampedMessageToFile()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "Logs"));
    var paths = new FakeAppPaths(root);
    var logger = new Logger(paths);
    logger.Initialize();

    logger.WriteLine("hello");
    logger.Dispose();

    var logFile = Assert.Single(Directory.GetFiles(paths.LogDirectory, "log*.txt"));
    var contents = File.ReadAllText(logFile);
    Assert.Contains("hello", contents);
    Assert.Contains("[", contents);
}
```

- [ ] **Step 2: Run layout/logger tests and verify RED**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter "MainWindowLayoutTests|LoggerTests"`

Expected: FAIL because the old XAML still binds `LogMessages` and Logger still has the in-memory collection.

- [ ] **Step 3: Make Logger file-only**

Replace `Logger` with a constructor that only receives `IAppPaths`, remove `IUiDispatcher`, `ObservableCollection`, and `MaxLogEntries`, and reduce `WriteLine` to:

```csharp
public void WriteLine<T>(T msg)
{
    Log.Information("[{Timestamp}]{Message}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg?.ToString());
}
```

Update Logger tests and any direct test construction to `new Logger(paths)`.

- [ ] **Step 4: Replace the main-window log panel with the status workspace**

In `MainWindow.axaml`, remove the behavior namespace and log `ItemsControl`. Increase the default window to 980×560 and add:

```xml
<Window.Styles>
  <Style Selector="Border.status">
    <Setter Property="Background" Value="#4A4A4A" />
  </Style>
  <Style Selector="Border.status TextBlock">
    <Setter Property="Foreground" Value="#E0E0E0" />
  </Style>
  <Style Selector="Border.status.racing">
    <Setter Property="Background" Value="#234C73" />
  </Style>
  <Style Selector="Border.status.backoff">
    <Setter Property="Background" Value="#6B531E" />
  </Style>
  <Style Selector="Border.status.success">
    <Setter Property="Background" Value="#1F6339" />
  </Style>
  <Style Selector="Border.status.failure">
    <Setter Property="Background" Value="#712E35" />
  </Style>
</Window.Styles>

<Grid Grid.Column="1" Margin="16,0,0,0" RowDefinitions="Auto,Auto,*,Auto" RowSpacing="10">
  <Border Background="#2E2E2E" CornerRadius="8" Padding="12">
    <Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto,Auto" ColumnSpacing="12">
      <TextBlock Text="选课状态" Foreground="White" FontSize="18" FontWeight="SemiBold" />
      <TextBlock Grid.Column="1" Text="总计" Foreground="#BDBDBD" />
      <TextBlock Grid.Column="2" Text="{Binding TotalCount}" Foreground="White" />
      <TextBlock Grid.Column="3" Text="{Binding RunningCount, StringFormat='进行中 {0}'}" Foreground="#72B7FF" />
      <TextBlock Grid.Column="4" Text="{Binding SucceededCount, StringFormat='成功 {0}'}" Foreground="#6AD58B" />
      <TextBlock Grid.Column="5" Text="{Binding FailedCount, StringFormat='失败 {0}'}" Foreground="#FF7A7A" />
    </Grid>
  </Border>

  <Border Grid.Row="1" IsVisible="{Binding IsBannerVisible}" Background="#3B3421" CornerRadius="6" Padding="10">
    <TextBlock Text="{Binding BannerText}" Foreground="#FFD37A" TextWrapping="Wrap" />
  </Border>

  <DataGrid Grid.Row="2"
            ItemsSource="{Binding CourseStatuses}"
            AutoGenerateColumns="False"
            IsReadOnly="True"
            GridLinesVisibility="Horizontal"
            HeadersVisibility="Column">
    <DataGrid.Columns>
      <DataGridTextColumn Header="课程" Binding="{Binding CourseName}" Width="2*" />
      <DataGridTemplateColumn Header="状态" Width="100">
        <DataGridTemplateColumn.CellTemplate>
          <DataTemplate>
            <Border Classes="status"
                    Classes.waiting="{Binding IsWaiting}"
                    Classes.racing="{Binding IsRacing}"
                    Classes.backoff="{Binding IsBackingOff}"
                    Classes.success="{Binding IsSucceeded}"
                    Classes.failure="{Binding IsFailed}"
                    CornerRadius="10" Padding="8,3">
              <TextBlock Text="{Binding StateText}" HorizontalAlignment="Center" />
            </Border>
          </DataTemplate>
        </DataGridTemplateColumn.CellTemplate>
      </DataGridTemplateColumn>
      <DataGridTextColumn Header="尝试次数" Binding="{Binding AttemptCount}" Width="85" />
      <DataGridTextColumn Header="耗时" Binding="{Binding ElapsedText}" Width="85" />
      <DataGridTextColumn Header="最新结果" Binding="{Binding LatestResult}" Width="2*" />
    </DataGrid.Columns>
  </DataGrid>

  <ProgressBar Grid.Row="3" IsVisible="{Binding IsProgressVisible}" Value="{Binding ProgressValue}" />
</Grid>
```

The base `Border.status` style covers waiting/cancelled rows; the class-specific styles cover racing, backoff, success, and failure. State text remains visible so color is never the only signal.

- [ ] **Step 5: Add Start/Stop button state**

In `LoginControl.axaml`, bind Start to `CanStartSelection` and add Stop:

```xml
<Button Content="开始选课"
        Height="32"
        IsVisible="{Binding AreAfterLoginButtonsVisible}"
        IsEnabled="{Binding CanStartSelection}"
        Command="{Binding StartSelectCourseCommand}" />
<Button Content="停止选课"
        Height="32"
        IsVisible="{Binding IsSelectionRunning}"
        Command="{Binding StopSelectCourseCommand}" />
```

Delete the obsolete selection-finished message and auto-scroll behavior after confirming no references remain with:

Run: `rg -n "SelectCourseFinishedMessage|AutoScrollToEndBehavior|LogMessages" iCourse iCourse.Tests`

Expected: no matches.

- [ ] **Step 6: Run UI contracts, all tests, and XAML compilation**

Run: `dotnet test iCourse.Tests/iCourse.Tests.csproj --configuration Release --filter "MainWindowLayoutTests|LoggerTests|MainWindowViewModelTests"`

Expected: PASS.

Run: `dotnet build iCourse/iCourse.csproj --configuration Release`

Expected: PASS with no Avalonia XAML errors.

- [ ] **Step 7: Commit UI and logging work**

```bash
git add iCourse/Views/MainWindow.axaml iCourse/Views/LoginControl.axaml iCourse/Helpers/Logger.cs iCourse.Tests/Helpers/LoggerTests.cs iCourse.Tests/Views/MainWindowLayoutTests.cs iCourse/Messages/SelectCourseFinishedMessage.cs iCourse/Behaviors/AutoScrollToEndBehavior.cs
git commit -m "feat: show per-course selection status"
```

### Task 6: Full verification and regression audit

**Files:**
- Verify only; modify a failing file only when a command identifies a concrete defect.

- [ ] **Step 1: Run the complete test suite**

Run: `dotnet test --configuration Release`

Expected: all tests pass, including existing credentials, logger, packaging-adjacent, view-model, classifier, transport, engine, and layout tests.

- [ ] **Step 2: Build the application**

Run: `dotnet build iCourse/iCourse.csproj --configuration Release --no-restore`

Expected: build succeeds with 0 errors and no Avalonia binding/XAML compilation failures.

- [ ] **Step 3: Run repository contract tests**

Run: `python3 -m unittest tests/test_packaging_contract.py`

Expected: all packaging and .NET target contracts pass.

- [ ] **Step 4: Audit concurrency and obsolete code**

Run:

```bash
rg -n "ConcurrentQueue|Task\.Factory\.StartNew|StartWorkerPool|TrySelectCourseWithBackoffAsync|SelectCourseFinishedMessage|AutoScrollToEndBehavior|LogMessages" iCourse iCourse.Tests
```

Expected: no matches.

Run:

```bash
rg -n "LanesPerCourse = 2|MaxConcurrency = 20|UnknownResponseLimit = 5|CourseSelectionStatusChangedMessage|StopSelectClass" iCourse iCourse.Tests
```

Expected: each required contract has production and/or test coverage.

- [ ] **Step 5: Inspect worktree scope**

Run: `git status --short && git diff --check && git log --oneline --decorate -6`

Expected: only the user's pre-existing `.DS_Store` files remain untracked; no whitespace errors; feature commits are present on `codex/course-selection-optimization`.
