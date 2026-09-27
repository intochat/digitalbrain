using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalBrain.Tests;

// Stands in for the sandbox host's HTTP API: runs start Running and stop as Exited(0); a test can end
// the latest run with any exit code or lose every run, as a replaced container would.
internal sealed class FakeSandbox : HttpMessageHandler
{
    public const string Url = "http://sandbox.test/";

    private readonly ConcurrentDictionary<string, (string Status, int? ExitCode)> _runs = new(StringComparer.Ordinal);
    private string _latest = "";

    public ConcurrentQueue<(string Method, string Path, JsonNode? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Enqueue((request.Method.Method, path, body));
        var segments = path.Split('/');
        if (request.Method == HttpMethod.Post && segments is ["runs", var identifier])
        {
            _runs[identifier] = ("Running", null);
            _latest = identifier;
            return Status(HttpStatusCode.Accepted, identifier);
        }
        if (segments is ["runs", var run] && _runs.ContainsKey(run)) { return Status(HttpStatusCode.OK, run); }
        if (segments is ["runs", var stopped, "stop"] && _runs.ContainsKey(stopped))
        {
            _runs[stopped] = ("Exited", 0);
            return Status(HttpStatusCode.OK, stopped);
        }
        if (segments is ["runs", var logged, "logs"] && _runs.ContainsKey(logged)) { return new(HttpStatusCode.OK) { Content = new StringContent("hi") }; }
        return new(HttpStatusCode.NotFound);
    }

    public int Started => Requests.Count(IsStart);

    public static bool IsStart((string Method, string Path, JsonNode? Body) request) => request.Method == "POST" && request.Path.Split('/') is ["runs", _];

    public void ExitLatest(int exitCode) => _runs[_latest] = ("Exited", exitCode);

    public void LoseEveryRun() => _runs.Clear();

    private HttpResponseMessage Status(HttpStatusCode code, string identifier)
    {
        var (status, exitCode) = _runs[identifier];
        return new(code)
        {
            Content = JsonContent.Create(new { identifier, status, exitCode, startedAt = DateTimeOffset.UnixEpoch },
                options: JsonSerializerOptions.Web),
        };
    }
}
