using System.Collections.Concurrent;
using System.Net;
using iCourse.Helpers;
using iCourse.Tests.Fakes;

namespace iCourse.Tests.Helpers;

public sealed class HttpTests
{
    [Fact]
    public async Task HttpPostAsync_ExternalCancellationStopsRetrying()
    {
        var handler = new CancellableHandler();
        using var logger = CreateLogger();
        using var client = new Http(TimeSpan.FromSeconds(5), logger, handler);
        using var cancellation = new CancellationTokenSource();

        var send = client.HttpPostAsync("test", null, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            send.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, handler.CallCount);
        Assert.True(handler.SawCancellation);
    }

    [Fact]
    public async Task Requests_UseDynamicHeaderSnapshotsWithoutChangingDefaults()
    {
        var handler = new HeaderCapturingHandler();
        using var logger = CreateLogger();
        using var client = new Http(TimeSpan.FromSeconds(5), logger, handler);

        client.SetOrigin("https://origin.test");
        client.SetReferer("https://referrer.test/first");
        var first = client.HttpPostAsync("first", null);
        await handler.FirstRequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(1));

        client.SetReferer("https://referrer.test/second");
        var second = client.HttpGetAsync("second");
        handler.ReleaseFirst.TrySetResult();
        await Task.WhenAll(first, second);

        var requests = handler.Requests.OrderBy(request => request.Path).ToList();
        Assert.Collection(
            requests,
            request =>
            {
                Assert.Equal("/first", request.Path);
                Assert.Equal("https://origin.test", request.Origin);
                Assert.Equal("https://referrer.test/first", request.Referrer);
            },
            request =>
            {
                Assert.Equal("/second", request.Path);
                Assert.Equal("https://origin.test", request.Origin);
                Assert.Equal("https://referrer.test/second", request.Referrer);
            });
        Assert.False(client.DefaultRequestHeaders.Contains("Origin"));
        Assert.Null(client.DefaultRequestHeaders.Referrer);
    }

    private static Logger CreateLogger()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Logs"));
        var logger = new Logger(
            new ImmediateUiDispatcher(),
            new FakeAppPaths(root));
        logger.Initialize();
        return logger;
    }

    private sealed class CancellableHandler : HttpMessageHandler
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SawCancellation { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("unreachable");
            }
            catch (OperationCanceledException)
            {
                SawCancellation = true;
                throw;
            }
        }
    }

    private sealed class HeaderCapturingHandler : HttpMessageHandler
    {
        private int callCount;

        public ConcurrentQueue<CapturedHeaders> Requests { get; } = new();

        public TaskCompletionSource FirstRequestSeen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCount);
            request.Headers.TryGetValues("Origin", out var origins);
            Requests.Enqueue(new CapturedHeaders(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                origins?.SingleOrDefault(),
                request.Headers.Referrer?.AbsoluteUri));

            if (call == 1)
            {
                FirstRequestSeen.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            };
        }
    }

    private sealed record CapturedHeaders(
        string Path,
        string? Origin,
        string? Referrer);
}
