using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IntoChat.Tests.Fixtures;

// Exercises the production OpenAI adapter. This fixture owns no application services.
public sealed class ScriptedModelServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<JsonElement> _requests = new();
    private int _completed;
    public ConcurrentQueue<string> Errors { get; } = new();
    public string Sql { get; set; } = "select id, company, email from public.leads where active = true order by id";
    public TimeSpan Delay { get; set; }
    public Func<CancellationToken, Task>? BeforeTable { get; set; }
    public TaskCompletionSource ToolRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScriptedModelServer(WebApplication app) { _app = app; }
    public Uri Endpoint => new(new Uri(_app.Urls.Single()), "/v1/");
    public IReadOnlyList<JsonElement> Requests => _requests.ToArray();
    public static async Task<ScriptedModelServer> StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var server = new ScriptedModelServer(app);
        app.MapPost("/v1/chat/completions", async (HttpContext http) =>
        {
            try { return await server.Respond(http); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                server.Errors.Enqueue(error.ToString());
                return Results.Problem("Scripted model protocol assertion failed.");
            }
        });
        await app.StartAsync(ct);
        return server;
    }
    private async Task<IResult> Respond(HttpContext http)
    {
        using var document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted);
        var request = document.RootElement.Clone();
        _requests.Enqueue(request);
        var messages = request.GetProperty("messages").EnumerateArray().ToArray();
        var results = messages.Where(m => m.GetProperty("role").GetString() == "tool").ToArray();
        object message;
        var reason = "tool_calls";
        if (results.Length == 0)
        {
            Assert.Contains(request.GetProperty("tools").EnumerateArray(), t => t.GetProperty("function").GetProperty("name").GetString() == "supabase_schema");
            message = Call("schema-call", "supabase_schema", "{\"table\":\"public.leads\"}");
        }
        else if (results.Length == 1)
        {
            Assert.Contains("company", results[0].GetProperty("content").GetString());
            ToolRequested.TrySetResult();
            if (BeforeTable is not null) { await BeforeTable(http.RequestAborted); }
            if (Delay > TimeSpan.Zero) { await Task.Delay(Delay, http.RequestAborted); }
            message = Call("table-call", "show_supabase_query_table", JsonSerializer.Serialize(new { title = "Active leads", sql = Sql }));
        }
        else
        {
            var content = results[^1].GetProperty("content").GetString()!;
            using var result = JsonDocument.Parse(content);
            // SDK tool serializer may retain CLR property casing.
            var window = result.RootElement.EnumerateObject().Single(p => p.Name.Equals("windowId", StringComparison.OrdinalIgnoreCase)).Value.GetString();
            Assert.StartsWith("table-", window);
            Interlocked.Increment(ref _completed);
            Completed.TrySetResult();
            message = new { role = "assistant", content = "Opened Active leads in your workspace." };
            reason = "stop";
        }
        return Results.Json(new { id = "fixture-response", @object = "chat.completion", created = 1, model = "gpt-5.6-luna", choices = new[] { new { index = 0, message, finish_reason = reason } }, usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 } });
    }
    private static object Call(string id, string name, string arguments) => new
    {
        role = "assistant", content = (string?)null,
        tool_calls = new[] { new { id, type = "function", function = new { name, arguments } } },
    };
    public void AssertCompleted()
    {
        Assert.Empty(Errors);
        Assert.True(_completed > 0, "The model never received a real query-window tool result.");
        Assert.Equal(_completed * 3, Requests.Count);
    }
    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
