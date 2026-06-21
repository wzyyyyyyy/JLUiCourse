using System.Net;
using System.Net.Http.Headers;
using iCourse.Models;
using iCourse.Services;

namespace iCourse.Tests.Services;

public sealed class CourseSelectionHttpTransportTests
{
    [Fact]
    public async Task SendAsync_CreatesFreshRequestAndContentWithExpectedHeadersAndForm()
    {
        var handler = new CapturingHandler((_, call) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"response-{call}")
        });
        using var client = CreateClient(handler);
        var transport = new CourseSelectionHttpTransport(client, "batch-42");
        var course = Course();

        var first = await transport.SendAsync(course, CancellationToken.None);
        var second = await transport.SendAsync(course, CancellationToken.None);

        Assert.Equal("response-1", first.Body);
        Assert.Equal("response-2", second.Body);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotSame(handler.Requests[0].Request, handler.Requests[1].Request);
        Assert.NotSame(handler.Requests[0].Content, handler.Requests[1].Content);

        foreach (var captured in handler.Requests)
        {
            Assert.Equal(HttpMethod.Post, captured.Method);
            Assert.Equal(
                "https://icourses.jlu.edu.cn/xsxk/sc/clazz/addxk",
                captured.Uri.AbsoluteUri);
            Assert.Equal(
                "https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId=batch-42",
                captured.Referrer.AbsoluteUri);
            Assert.Equal("https://icourses.jlu.edu.cn", captured.Origin);
            Assert.Equal(
                "clazzId=course%2F1&secretVal=secret+value&clazzType=XGKC",
                captured.Body);
            Assert.Equal("application/x-www-form-urlencoded", captured.ContentType);
        }

        Assert.Null(client.DefaultRequestHeaders.Referrer);
        Assert.False(client.DefaultRequestHeaders.Contains("Origin"));
    }

    [Fact]
    public async Task SendAsync_TooManyRequestsReturnsDeltaRetryAfter()
    {
        var expected = TimeSpan.FromSeconds(3);
        var handler = new CapturingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("slow down")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(expected);
            return response;
        });
        using var client = CreateClient(handler);
        var transport = new CourseSelectionHttpTransport(client, "batch");

        var result = await transport.SendAsync(Course(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal("slow down", result.Body);
        Assert.Equal(expected, result.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_DateRetryAfterReturnsDifferenceFromNow()
    {
        var retryDate = DateTimeOffset.UtcNow.AddSeconds(-2);
        var handler = new CapturingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("past retry date")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryDate);
            return response;
        });
        using var client = CreateClient(handler);
        var transport = new CourseSelectionHttpTransport(client, "batch");

        var result = await transport.SendAsync(Course(), CancellationToken.None);

        Assert.NotNull(result.RetryAfter);
        Assert.True(result.RetryAfter < TimeSpan.Zero);
        Assert.InRange(result.RetryAfter.Value, TimeSpan.FromSeconds(-5), TimeSpan.Zero);
    }

    [Fact]
    public async Task SendAsync_PropagatesCancellationToHttpSend()
    {
        var handler = new BlockingHandler();
        using var client = CreateClient(handler);
        var transport = new CourseSelectionHttpTransport(client, "batch");
        using var cancellation = new CancellationTokenSource();

        var send = transport.SendAsync(Course(), cancellation.Token);
        await handler.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.True(handler.SawCancellation);
    }

    [Fact]
    public async Task SendAsync_PropagatesCancellationToBodyRead()
    {
        var content = new CancellationAwareContent();
        var handler = new CapturingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var client = CreateClient(handler);
        var transport = new CourseSelectionHttpTransport(client, "batch");
        using var cancellation = new CancellationTokenSource();

        var send = transport.SendAsync(Course(), cancellation.Token);
        await content.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.True(content.ReadToken.CanBeCanceled);
        Assert.True(content.ReadToken.IsCancellationRequested);
    }

    [Fact]
    public async Task SendAsync_HttpClientTimeoutCoversBlockedBody()
    {
        var content = new CancellationAwareContent();
        var handler = new CapturingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var client = CreateClient(handler);
        client.Timeout = TimeSpan.FromMilliseconds(50);
        var transport = new CourseSelectionHttpTransport(client, "batch");
        using var cleanup = new CancellationTokenSource();

        var send = transport.SendAsync(Course(), cleanup.Token);
        await content.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            var completed = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(send, completed);
            await Assert.ThrowsAnyAsync<TaskCanceledException>(() => send);
            Assert.False(cleanup.IsCancellationRequested);
        }
        finally
        {
            cleanup.Cancel();
            try
            {
                await send;
            }
            catch
            {
                // The expected timeout or cleanup cancellation is asserted above.
            }
        }
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler)
        {
            BaseAddress = new Uri("https://icourses.jlu.edu.cn/")
        };

    private static Course Course() =>
        new()
        {
            CourseId = "course/1",
            Name = "Test Course",
            SecretVal = "secret value"
        };

    private sealed class CapturingHandler(
        Func<CancellationToken, int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int calls;

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            var content = request.Content
                ?? throw new InvalidOperationException("Request content is required.");
            var body = await content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("Origin", out var origins);

            Requests.Add(new CapturedRequest(
                request,
                content,
                request.Method,
                request.RequestUri ?? throw new InvalidOperationException("Request URI is required."),
                request.Headers.Referrer ?? throw new InvalidOperationException("Referrer is required."),
                Assert.Single(origins ?? []),
                body,
                content.Headers.ContentType?.MediaType));

            return responseFactory(cancellationToken, call);
        }
    }

    private sealed record CapturedRequest(
        HttpRequestMessage Request,
        HttpContent Content,
        HttpMethod Method,
        Uri Uri,
        Uri Referrer,
        string Origin,
        string Body,
        string? ContentType);

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SawCancellation { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendStarted.TrySetResult();
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

    private sealed class CancellationAwareContent : HttpContent
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ReadToken { get; private set; }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            throw new InvalidOperationException("The cancellable overload must be used.");

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            ReadToken = cancellationToken;
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
