using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalBrain.Tests;

// Stands in for the sandbox host's HTTP API: runs start Running and stop as Exited(0).
internal sealed class FakeSandbox : HttpMessageHandler
{
    public const string Url = "http://sandbox.test/";

    private readonly ConcurrentDictionary<string, string> _statuses = new(StringComparer.Ordinal);

    public ConcurrentQueue<(string Method, string Path, JsonNode? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Enqueue((request.Method.Method, path, body));
        var segments = path.Split('/');
        if (request.Method == HttpMethod.Post && path == "runs")
        {
            var identifier = request.RequestUri.Query.Split('=')[1];
            _statuses[identifier] = "Running";
            return Status(HttpStatusCode.Accepted, identifier);
        }
        if (segments is ["runs", var run] && _statuses.ContainsKey(run)) { return Status(HttpStatusCode.OK, run); }
        if (segments is ["runs", var stopped, "stop"] && _statuses.ContainsKey(stopped))
        {
            _statuses[stopped] = "Exited";
            return Status(HttpStatusCode.OK, stopped);
        }
        if (segments is ["runs", var logged, "logs"] && _statuses.ContainsKey(logged)) { return new(HttpStatusCode.OK) { Content = new StringContent("hi") }; }
        return new(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Status(HttpStatusCode code, string identifier)
    {
        var exited = _statuses[identifier] == "Exited";
        return new(code)
        {
            Content = JsonContent.Create(new { identifier, status = _statuses[identifier], exitCode = exited ? 0 : (int?)null, startedAt = DateTimeOffset.UnixEpoch },
                options: JsonSerializerOptions.Web),
        };
    }
}
