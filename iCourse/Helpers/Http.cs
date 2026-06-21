using Polly;
using Polly.Retry;
using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace iCourse.Helpers;

internal sealed class Http : HttpClient
{
    private readonly AsyncRetryPolicy retryPolicy;
    private readonly Logger logger;
    private readonly object dynamicHeadersGate = new();
    private string? origin;
    private Uri? referrer;

    public Http(TimeSpan timeout, Logger logger)
        : this(timeout, logger, CreateHandler())
    {
    }

    internal Http(TimeSpan timeout, Logger logger, HttpMessageHandler handler)
        : base(handler)
    {
        this.logger = logger;
        Timeout = timeout;
        BaseAddress = new Uri("https://icourses.jlu.edu.cn");

        DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
        DefaultRequestHeaders.Add("Host", "icourses.jlu.edu.cn");
        DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/103.0.5060.114 Safari/537.36 Edg/103.0.1264.62");

        retryPolicy = Policy
            .Handle<TaskCanceledException>()
            .Or<HttpRequestException>()
            .WaitAndRetryForeverAsync(
                retryAttempt =>
                {
                    var waitTime = TimeSpan.FromMilliseconds(Math.Pow(2, Math.Min(retryAttempt, 8)));
                    logger.WriteLine($"重试第 {retryAttempt} 次，等待时间 {waitTime.TotalSeconds} 秒");
                    return waitTime;
                },
                onRetry: (exception, timeSpan) =>
                {
                    logger.WriteLine(exception is TaskCanceledException
                        ? $"重试超时，等待 {timeSpan.TotalSeconds} 秒后重试。"
                        : $"发生异常：{exception.Message}，等待 {timeSpan.TotalSeconds} 秒后重试。");
                });
    }

    private static HttpMessageHandler CreateHandler() =>
        new HttpClientHandler
        {
            UseCookies = true,
            ServerCertificateCustomValidationCallback = (HttpRequestMessage request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) => true
        };

    public void SetOrigin(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (dynamicHeadersGate)
        {
            this.origin = origin;
        }
    }

    public void SetReferer(string referer)
    {
        ArgumentNullException.ThrowIfNull(referer);
        var uri = new Uri(referer);
        lock (dynamicHeadersGate)
        {
            referrer = uri;
        }
    }

    public void AddHeader(string key, string value)
    {
        DefaultRequestHeaders.Remove(key);
        DefaultRequestHeaders.Add(key, value);
    }

    public async Task<string> HttpGetAsync(string url)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyDynamicHeaders(request);

            using var response = await SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        });
    }

    public async Task<string> HttpPostAsync(string url, HttpContent? content)
    {
        return await HttpPostAsync(url, content, CancellationToken.None);
    }

    public async Task<string> HttpPostAsync(
        string url,
        HttpContent? content,
        CancellationToken token)
    {
        return await retryPolicy.ExecuteAsync(async cancellationToken =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            ApplyDynamicHeaders(request);

            using var response = await SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }, token);
    }

    private void ApplyDynamicHeaders(HttpRequestMessage request)
    {
        string? originSnapshot;
        Uri? referrerSnapshot;
        lock (dynamicHeadersGate)
        {
            originSnapshot = origin;
            referrerSnapshot = referrer;
        }

        if (originSnapshot is not null)
        {
            request.Headers.Add("Origin", originSnapshot);
        }

        request.Headers.Referrer = referrerSnapshot;
    }
}
