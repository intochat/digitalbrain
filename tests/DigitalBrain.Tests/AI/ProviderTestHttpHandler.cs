using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace DigitalBrain.Tests.AI;

internal sealed record RecordedProviderRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string[]> Headers,
    string Body);

internal sealed class ProviderTestHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedProviderRequest> _requests = new();

    public IReadOnlyList<RecordedProviderRequest> Requests => _requests.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        _requests.Enqueue(new RecordedProviderRequest(
            request.Method,
            request.RequestUri!,
            headers,
            body));

        return await respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return response;
    }

    public static HttpResponseMessage EventStream(string events)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(events, Encoding.UTF8)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return response;
    }
}
