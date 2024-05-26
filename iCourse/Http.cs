using Polly;
using Polly.Retry;
using System.Net.Http;

namespace iCourse
{
    class Http
    {
        private readonly HttpClient _client;
        private readonly AsyncRetryPolicy _retryPolicy;

        public Http(TimeSpan timeout)
        {
            _client = new HttpClient
            {
                Timeout = timeout,
                BaseAddress = new Uri("https://icourses.jlu.edu.cn")
            };
            _client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
            _client.DefaultRequestHeaders.Add("Host", "icourses.jlu.edu.cn");
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/103.0.5060.114 Safari/537.36 Edg/103.0.1264.62");

            _retryPolicy = Policy
          .Handle<TaskCanceledException>()
          .Or<HttpRequestException>()
          .WaitAndRetryForeverAsync(
              retryAttempt =>
              {
                  var waitTime = TimeSpan.FromMilliseconds(Math.Pow(2, Math.Min(retryAttempt, 8))); // 上限为256ms
                  MainWindow.Instance.WriteLine($"重试第 {retryAttempt} 次，等待时间 {waitTime.TotalSeconds} 秒");
                  return waitTime;
              },
              onRetry: (exception, timeSpan) =>
              {
                  if (exception is TaskCanceledException)
                  {
                      MainWindow.Instance.WriteLine($"重试超时，等待 {timeSpan.TotalSeconds} 秒后重试。");
                  }
                  else
                  {
                      MainWindow.Instance.WriteLine($"发生异常：{exception.Message}，等待 {timeSpan.TotalSeconds} 秒后重试。");
                  }
              });
        }

        public void SetOrigin(string origin)
        {
            if (_client.DefaultRequestHeaders.Contains("Origin"))
            {
                _client.DefaultRequestHeaders.Remove("Origin");
            }
            _client.DefaultRequestHeaders.Add("Origin", origin);
        }

        public void SetReferer(string referer)
        {
            if (_client.DefaultRequestHeaders.Contains("Referer"))
            {
                _client.DefaultRequestHeaders.Remove("Referer");
            }
            _client.DefaultRequestHeaders.Add("Referer", referer);
        }

        public async Task<string> HttpGetAsync(string url)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                using (var response = await _client.SendAsync(request))
                {

                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            });
        }

        public async Task<string> HttpPostAsync(string url, HttpContent content)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };

                using (var response = await _client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            });
        }
    }
}
