using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IntoChat.Tests.E2E.Agent;

// Exercises the production OpenAI adapter. This fixture owns no application services.
public sealed partial class ScriptedModelServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<JsonElement> _requests = new();
    private int _completed;
    public ConcurrentQueue<string> Errors { get; } = new();
    public string Sql { get; set; } = "select id, company, email from public.leads where active = true order by id";
    public string? RepairSql { get; set; }
    public int RepairCount { get; private set; }
    public string? ExpectedValidationError { get; set; }
    public TimeSpan Delay { get; set; }
    public Func<CancellationToken, Task>? BeforeTable { get; set; }
    // Refine/read turns are keyed by the user's words: "only …" refines, "how many" counts.
    public string? RefineColumn { get; set; }
    public string RefineOperator { get; set; } = "eq";
    public string? RefineValue { get; set; }
    public int? LastReadFilteredRows { get; private set; }
    public string? LastReadAggregate { get; private set; }
    public bool Refined { get; private set; }
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
        var lastUser = messages.LastOrDefault(m => m.GetProperty("role").GetString() == "user") is { } user
            ? user.GetProperty("content").GetString() ?? ""
            : "";
        // The client appends an artifact-context paragraph to the owner's words; intent comes from the owner's own text.
        var ownerText = lastUser.Split("\n\n[Conversation agent:", 2, StringSplitOptions.None)[0];
        var wantsCount = ownerText.Contains("how many", StringComparison.OrdinalIgnoreCase);
        var wantsRefine = RefineValue is not null && ownerText.Contains("only", StringComparison.OrdinalIgnoreCase);
        object message;
        var reason = "tool_calls";
        if (results.Length == 0)
        {
            if (wantsCount)
            {
                message = Call("count-call", "table_read", JsonSerializer.Serialize(new
                {
                    tableId = WindowId(messages),
                    aggregate = "count",
                    aggregateColumn = (string?)null,
                    columns = Array.Empty<string>(),
                    limit = 25,
                }));
            }
            else if (wantsRefine)
            {
                message = Call("refine-call", "table_refine", JsonSerializer.Serialize(new
                {
                    tableId = WindowId(messages),
                    filters = new[] { new { columnId = RefineColumn, @operator = RefineOperator, value = RefineValue } },
                    sortColumn = (string?)null,
                    sortDescending = false,
                    visibleColumns = Array.Empty<string>(),
                }));
            }
            else
            {
                Assert.Contains(request.GetProperty("tools").EnumerateArray(), t => t.GetProperty("function").GetProperty("name").GetString() == "supabase_schema");
                message = Call("schema-call", "supabase_schema", "{\"table\":\"public.leads\"}");
            }
        }
        else if (results.Length == 1 && (wantsCount || wantsRefine))
        {
            var content = results[0].GetProperty("content").GetString()!;
            using var parsed = JsonDocument.Parse(content);
            Assert.Equal(0, ParseProperty(parsed.RootElement, "rows").GetArrayLength());
            if (wantsCount)
            {
                Assert.Equal(0, ParseProperty(parsed.RootElement, "rowsRead").GetInt32());
                LastReadAggregate = ParseProperty(ParseProperty(parsed.RootElement, "aggregate"), "Value").GetString();
                LastReadFilteredRows = ParseProperty(parsed.RootElement, "filteredRows").GetInt32();
                message = new { role = "assistant", content = LastReadFilteredRows + " in London" };
            }
            else
            {
                Refined = true;
                message = new { role = "assistant", content = "Refined the same window to London." };
            }
            reason = "stop";
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
            if (result.RootElement.TryGetProperty("isError", out var isError) && isError.GetBoolean())
            {
                var error = result.RootElement.GetProperty("message").GetString()!;
                if (ExpectedValidationError is not null) { Assert.Contains(ExpectedValidationError, error); }
                if (RepairSql is not null && RepairCount == 0)
                {
                    RepairCount++;
                    message = Call("repair-table-call", "show_supabase_query_table", JsonSerializer.Serialize(new { title = "Active leads", sql = RepairSql }));
                }
                else
                {
                    message = new { role = "assistant", content = "Could not open the table: " + error };
                    reason = "stop";
                }
            }
            else
            {
                // SDK tool serializer may retain CLR property casing.
                var window = ParseProperty(result.RootElement, "windowId").GetString();
                Assert.StartsWith("table-", window);
                Interlocked.Increment(ref _completed);
                Completed.TrySetResult();
                message = new { role = "assistant", content = "Opened Active leads in your workspace." };
                reason = "stop";
            }
        }
        return Results.Json(new { id = "fixture-response", @object = "chat.completion", created = 1, model = "gpt-5.6-luna", choices = new[] { new { index = 0, message, finish_reason = reason } }, usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 } });
    }

    private static JsonElement ParseProperty(JsonElement element, string name)
        => element.EnumerateObject().Single(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    // The open tool records the window id on the assistant turn; later turns read it from there.
    private static string WindowId(JsonElement[] messages)
    {
        foreach (var message in messages.Reverse())
        {
            if (message.GetProperty("role").GetString() != "assistant") { continue; }
            var content = message.GetProperty("content").GetString() ?? "";
            var match = TableIdPattern().Match(content);
            if (match.Success) { return match.Value; }
        }
        throw new InvalidOperationException("No live-table window id was found in the conversation history.");
    }

    private static object Call(string id, string name, string arguments) => new
    {
        role = "assistant",
        content = (string?)null,
        tool_calls = new[] { new { id, type = "function", function = new { name, arguments } } },
    };
    public void AssertCompleted()
    {
        Assert.Empty(Errors);
        Assert.True(_completed > 0, "The model never received a real query-window tool result.");
        Assert.Equal(_completed * 3 + RepairCount, Requests.Count);
    }
    public void AssertNoProtocolErrors() => Assert.Empty(Errors);
    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [GeneratedRegex("table-[0-9a-f]+")]
    private static partial Regex TableIdPattern();
}