using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using iCourse.Messages;
using iCourse.Models;
using iCourse.Services;
using iCourse.Tests.Fakes;

namespace iCourse.Tests.Helpers;

public sealed class JLUiCourseApiTests
{
    [Fact]
    public async Task StartSelectClassAsync_StopCancelsFavoritePrefetchAndCompletesOnce()
    {
        var handler = new ReleasableFavoriteHandler();
        using var fixture = CreateFixture(handler);

        var run = fixture.Api.StartSelectClassAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        fixture.Api.StopSelectClass();

        try
        {
            var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(run, completed);
            await run;
        }
        finally
        {
            handler.Release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(1, handler.CallCount);
        Assert.True(handler.SawCancellation);
        var completion = Assert.Single(fixture.Completed);
        Assert.True(completion.WasCancelled);
    }

    [Fact]
    public async Task StartSelectClassAsync_FavoriteBusinessFailureSendsSpecificError()
    {
        var handler = new StaticResponseHandler(
            "{\"code\":500,\"msg\":\"收藏服务不可用\"}");
        using var fixture = CreateFixture(handler);

        await fixture.Api.StartSelectClassAsync();

        var banner = Assert.Single(fixture.Banners);
        Assert.Equal("读取收藏失败：收藏服务不可用", banner.Text);
        Assert.Equal(SystemBannerSeverity.Error, banner.Severity);
        Assert.Single(fixture.Completed);
    }

    private static ApiFixture CreateFixture(HttpMessageHandler handler)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Logs"));
        var paths = new FakeAppPaths(root);
        var logger = new Logger(paths);
        logger.Initialize();
        var messenger = new StrongReferenceMessenger();
        var api = new JLUiCourseApi(
            logger,
            new UserCredentials(paths),
            new FakeDialogService(),
            new FakeAppLifetime(),
            new CourseSelectionEngine(
                new CourseSelectionResponseClassifier(),
                new AggressiveCourseSelectionDelay(),
                new CourseSelectionOptions()),
            messenger);
        var http = new Http(TimeSpan.FromSeconds(5), logger, handler);
        SetPrivateField(api, "client", http);
        SetPrivateField(api, "batch", new BatchInfo { batchId = "batch" });
        return new ApiFixture(api, http, logger, messenger);
    }

    private static void SetPrivateField<T>(JLUiCourseApi api, string name, T value)
    {
        var field = typeof(JLUiCourseApi).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field {name}.");
        field.SetValue(api, value);
    }

    private sealed class ApiFixture : IDisposable
    {
        private readonly object recipient = new();
        private readonly Http http;
        private readonly Logger logger;
        private readonly IMessenger messenger;

        public ApiFixture(
            JLUiCourseApi api,
            Http http,
            Logger logger,
            IMessenger messenger)
        {
            Api = api;
            this.http = http;
            this.logger = logger;
            this.messenger = messenger;
            messenger.Register<SystemBannerMessage>(
                recipient,
                (_, message) => Banners.Enqueue(message));
            messenger.Register<CourseSelectionRunCompletedMessage>(
                recipient,
                (_, message) => Completed.Enqueue(message));
        }

        public JLUiCourseApi Api { get; }

        public ConcurrentQueue<SystemBannerMessage> Banners { get; } = new();

        public ConcurrentQueue<CourseSelectionRunCompletedMessage> Completed { get; } = new();

        public void Dispose()
        {
            messenger.UnregisterAll(recipient);
            http.Dispose();
            logger.Dispose();
        }
    }

    private sealed class ReleasableFavoriteHandler : HttpMessageHandler
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SawCancellation { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Started.TrySetResult();
            var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var completed = await Task.WhenAny(cancellation, Release.Task);
            if (completed == cancellation)
            {
                try
                {
                    await cancellation;
                }
                catch (OperationCanceledException)
                {
                    SawCancellation = true;
                    throw;
                }
            }

            return Response("{\"code\":200,\"data\":[]}");
        }
    }

    private sealed class StaticResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Response(body));
    }

    private static HttpResponseMessage Response(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeDialogService : IDialogService
    {
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<string?> ShowCaptchaAsync(string base64Image) => Task.FromResult<string?>(null);
        public Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches) => Task.FromResult<BatchInfo?>(null);
        public Task ShowQueryCoursesAsync() => Task.CompletedTask;
    }

    private sealed class FakeAppLifetime : IAppLifetime
    {
        public void Shutdown()
        {
        }

        public void Restart()
        {
        }
    }
}
