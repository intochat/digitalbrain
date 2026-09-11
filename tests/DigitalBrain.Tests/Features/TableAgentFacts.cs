using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TableAgentFacts
{
    private const string Products = """
        {"title":"Products","columns":[{"id":"name","label":"Name","type":"text"},{"id":"price","label":"Price","type":"number"}],
         "rows":[{"id":"a","cells":["Mouse",25]},{"id":"b","cells":["Cable",9]},{"id":"c","cells":["Adapter",100]}]}
        """;

    [Fact]
    public async Task Agent_creates_a_table_and_reads_filters_changed_through_http()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        using var model = new ScriptedChatClient();
        model.CallTool("create_table", "{\"table\":" + Products + "}");
        model.Say("Here is your interactive table.");
        await using var app = await StartAsync(brain, model);
        using var client = app.GetTestClient();
        var first = await RunAsync(client, "Generate products");
        var created = ToolResult(first);
        Assert.Equal("table", created.GetProperty("kind").GetString());
        Assert.Equal(3, created.GetProperty("totalRows").GetInt32());
        var id = created.GetProperty("id").GetString()!;

        using var update = await client.PutAsJsonAsync($"/kit/tables/{id}/view", new
        {
            expectedRevision = created.GetProperty("revision").GetInt64(),
            filters = new[] { new { columnId = "price", @operator = "lt", value = 20 } },
            sort = (object?)null,
            visibleColumns = new[] { "name", "price" },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        model.CallTool("read_table", JsonSerializer.Serialize(new { id, offset = 0, limit = 50 }));
        model.Say("One product matches: Cable.");
        var second = await RunAsync(client, $"What is visible in table {id}?");
        var read = ToolResult(second);
        Assert.Equal(1, read.GetProperty("filteredRows").GetInt32());
        Assert.Equal("b", read.GetProperty("rows")[0].GetProperty("id").GetString());
        Assert.Equal(3, read.GetProperty("totalRows").GetInt32());
        Assert.Equal("lt", read.GetProperty("filters")[0].GetProperty("operator").GetString());
        Assert.Contains(model.Calls.Last().SelectMany(message => message.Contents), content =>
            content is FunctionResultContent result && JsonSerializer.Serialize(result.Result).Contains("filteredRows", StringComparison.Ordinal));

        using var stale = await client.PutAsJsonAsync($"/kit/tables/{id}/view", new
        {
            expectedRevision = created.GetProperty("revision").GetInt64(),
            filters = Array.Empty<object>(), sort = (object?)null, visibleColumns = new[] { "name", "price" },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        model.CallTool("update_table_view", JsonSerializer.Serialize(new
        {
            id,
            view = new
            {
                expectedRevision = read.GetProperty("revision").GetInt64(),
                filters = Array.Empty<object>(),
                sort = new { columnId = "price", descending = true },
                visibleColumns = new[] { "price" },
            },
        }));
        model.Say("Cleared the filter and sorted prices descending.");
        var changed = ToolResult(await RunAsync(client, "Show all prices highest first"));
        Assert.Equal(3, changed.GetProperty("filteredRows").GetInt32());
        Assert.Empty(changed.GetProperty("filters").EnumerateArray());
        Assert.Equal("c", changed.GetProperty("rows")[0].GetProperty("id").GetString());
        Assert.Equal("price", Assert.Single(changed.GetProperty("visibleColumns").EnumerateArray()).GetString());

        model.CallTool("update_table_view", JsonSerializer.Serialize(new
        {
            id,
            view = new
            {
                expectedRevision = read.GetProperty("revision").GetInt64(),
                filters = Array.Empty<object>(), sort = (object?)null, visibleColumns = new[] { "name", "price" },
            },
        }));
        model.Say("That change conflicted; I need to read the table again.");
        var conflict = ToolResult(await RunAsync(client, "Use an outdated revision"));
        Assert.Equal("tableError", conflict.GetProperty("kind").GetString());
        Assert.Equal("revision_conflict", conflict.GetProperty("code").GetString());

        var saved = await client.GetFromJsonAsync<JsonElement>("/kit/tables", TestContext.Current.CancellationToken);
        Assert.Contains(saved.EnumerateArray(), table => table.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Table_routes_reject_invalid_input_and_require_authentication()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        using var model = new ScriptedChatClient();
        await using var app = await StartAsync(brain, model, gated: true);
        using var client = app.GetTestClient();
        using var unauthorized = await client.GetAsync("/kit/tables", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("owner:test-password")));
        using var missing = await client.GetAsync("/kit/tables/missing", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var bad = await client.PostAsJsonAsync("/kit/tables", new { title = "Bad", columns = Array.Empty<object>(), rows = Array.Empty<object>() }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(BrainSimulation brain, IChatClient model, bool gated = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (gated)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BasicAuthGate.UsernameConfigurationKey] = "owner",
                [BasicAuthGate.PasswordConfigurationKey] = "test-password",
            });
        }
        builder.Services.AddSingleton(brain.SiloServices.GetRequiredService<TableService>());
        builder.Services.AddSingleton(new ChatClientBuilder(model).UseFunctionInvocation().Build());
        builder.AddConversationalAgent();
        var app = builder.Build();
        app.UseBasicAuthGate();
        app.MapConversationalAgent();
        app.MapTableEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<List<JsonElement>> RunAsync(HttpClient client, string text)
    {
        using var response = await client.PostAsJsonAsync("/agent", new
        {
            threadId = Guid.NewGuid().ToString(), runId = Guid.NewGuid().ToString(),
            messages = new[] { new { id = Guid.NewGuid().ToString(), role = "user", content = text } },
            tools = Array.Empty<object>(), context = Array.Empty<object>(), state = new { }, forwardedProps = new { },
        }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, body);
        var events = body.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[5..])).ToList();
        Assert.Equal("RUN_FINISHED", events[^1].GetProperty("type").GetString());
        return events;
    }

    private static JsonElement ToolResult(List<JsonElement> events) => JsonSerializer.Deserialize<JsonElement>(
        events.Single(item => item.GetProperty("type").GetString() == "TOOL_CALL_RESULT").GetProperty("content").GetString()!);
}
