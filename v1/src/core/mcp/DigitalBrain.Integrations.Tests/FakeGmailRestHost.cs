using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Integrations.Tests;

internal sealed class FakeGmailRestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, MessageRecord> _messages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HttpStatusCode> _getStatusById = new(StringComparer.Ordinal);

    private FakeGmailRestHost(WebApplication app, Uri baseUri)
    {
        _app = app;
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; }

    public HttpStatusCode GetStatusCode { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode ListStatusCode { get; set; } = HttpStatusCode.OK;

    public string? GetErrorBody { get; set; }

    public static async Task<FakeGmailRestHost> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        FakeGmailRestHost? host = null;

        app.MapGet("/gmail/v1/users/{userId}/messages", (string userId, HttpRequest request) =>
        {
            if (host is null)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            if (host.ListStatusCode != HttpStatusCode.OK)
            {
                return Results.Json(
                    new { error = new { message = "list failed" } },
                    statusCode: (int)host.ListStatusCode);
            }

            var q = request.Query["q"].ToString();
            var maxResults = 10;
            if (int.TryParse(request.Query["maxResults"], out var parsed) && parsed > 0)
            {
                maxResults = parsed;
            }

            var matches = host._messages.Values
                .Where(message => string.IsNullOrWhiteSpace(q) || MessageRecord.MatchesQuery(q))
                .Take(maxResults)
                .Select(message => new { id = message.Id, threadId = message.ThreadId })
                .ToArray();

            return Results.Json(new { messages = matches, resultSizeEstimate = matches.Length });
        });

        app.MapGet("/gmail/v1/users/{userId}/messages/{id}", (string userId, string id, HttpRequest request) =>
        {
            if (host is null)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            var status = host._getStatusById.TryGetValue(id, out var byId)
                ? byId
                : host.GetStatusCode;
            if (status != HttpStatusCode.OK)
            {
                return Results.Json(
                    host.GetErrorBody is null
                        ? new { error = new { message = "get failed" } }
                        : JsonSerializer.Deserialize<object>(host.GetErrorBody),
                    statusCode: (int)status);
            }

            if (!host._messages.TryGetValue(id, out var message))
            {
                return Results.Json(
                    new { error = new { message = $"message {id} not found" } },
                    statusCode: StatusCodes.Status404NotFound);
            }

            var format = request.Query["format"].ToString();
            return Results.Json(message.ToJson(format));
        });

        app.MapGet("/gmail/v1/users/{userId}/threads", () =>
            Results.Json(new { threads = Array.Empty<object>(), resultSizeEstimate = 0 }));

        app.MapGet("/gmail/v1/users/{userId}/threads/{id}", (string id) =>
            Results.Json(new { id, messages = Array.Empty<object>() }));

        app.MapGet("/gmail/v1/users/{userId}/labels", () =>
            Results.Json(new { labels = Array.Empty<object>() }));

        await app.StartAsync();
        var address = app.Urls.Single();
        host = new FakeGmailRestHost(app, new Uri(address.TrimEnd('/') + "/"));
        return host;
    }

    public void SeedMessage(
        string id,
        string subject,
        string sender,
        string plaintextBody,
        string? responseId = null)
    {
        _messages[id] = new MessageRecord(
            responseId ?? id,
            id,
            subject,
            sender,
            plaintextBody);
    }

    public void SeedMessageMissingId(string requestedId, string subject, string sender, string plaintextBody)
    {
        _messages[requestedId] = new MessageRecord(
            Id: null,
            RequestedKey: requestedId,
            subject,
            sender,
            plaintextBody);
    }

    public void SetGetStatus(string id, HttpStatusCode status)
        => _getStatusById[id] = status;

    public void Clear()
    {
        _messages.Clear();
        _getStatusById.Clear();
        GetStatusCode = HttpStatusCode.OK;
        ListStatusCode = HttpStatusCode.OK;
        GetErrorBody = null;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private sealed record MessageRecord(
        string? Id,
        string RequestedKey,
        string Subject,
        string Sender,
        string PlaintextBody)
    {
        public string ThreadId => $"thread-{RequestedKey}";

        public static bool MatchesQuery(string query) => true;

        public object ToJson(string format)
        {
            var metadataOnly = string.Equals(format, "metadata", StringComparison.OrdinalIgnoreCase)
                || string.Equals(format, "METADATA", StringComparison.OrdinalIgnoreCase);

            var headers = new[]
            {
                new { name = "Subject", value = Subject },
                new { name = "From", value = Sender },
            };

            if (metadataOnly)
            {
                return new
                {
                    id = Id,
                    threadId = ThreadId,
                    snippet = PlaintextBody,
                    payload = new
                    {
                        mimeType = "text/plain",
                        headers,
                    },
                };
            }

            var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(PlaintextBody))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            return new
            {
                id = Id,
                threadId = ThreadId,
                snippet = PlaintextBody,
                payload = new
                {
                    mimeType = "text/plain",
                    headers,
                    body = new { data, size = PlaintextBody.Length },
                },
            };
        }
    }
}
