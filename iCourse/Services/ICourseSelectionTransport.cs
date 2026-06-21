using iCourse.Models;

namespace iCourse.Services;

public interface ICourseSelectionTransport
{
    Task<CourseSelectionAttempt> SendAsync(Course course, CancellationToken token);
}

public sealed class CourseSelectionHttpTransport : ICourseSelectionTransport
{
    private const string Origin = "https://icourses.jlu.edu.cn";
    private const string SelectionEndpoint = "xsxk/sc/clazz/addxk";

    private readonly HttpClient client;
    private readonly Uri referrer;

    public CourseSelectionHttpTransport(HttpClient client, string batchId)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(batchId);

        this.client = client;
        referrer = new Uri(
            $"{Origin}/xsxk/elective/grablessons?batchId={batchId}");
    }

    public async Task<CourseSelectionAttempt> SendAsync(
        Course course,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(course);

        using var request = new HttpRequestMessage(HttpMethod.Post, SelectionEndpoint)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["clazzId"] = course.CourseId,
                    ["secretVal"] = course.SecretVal,
                    ["clazzType"] = "XGKC"
                })
        };
        request.Headers.Referrer = referrer;
        request.Headers.Add("Origin", Origin);

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (client.Timeout != Timeout.InfiniteTimeSpan)
        {
            requestTimeout.CancelAfter(client.Timeout);
        }

        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestTimeout.Token)
            .ConfigureAwait(false);
        var body = await response.Content
            .ReadAsStringAsync(requestTimeout.Token)
            .ConfigureAwait(false);
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
        {
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        }

        return new CourseSelectionAttempt(
            response.StatusCode,
            body,
            retryAfter);
    }
}
