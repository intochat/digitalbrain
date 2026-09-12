using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.ClickHouse;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

// The existing table tools and routes work on a ClickHouse query table through the chtable- prefix.
public sealed class ClickHouseTableAgentFacts
{
    [Fact]
    public async Task Agent_reads_filters_and_lists_a_query_table_through_the_existing_table_tools()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule), typeof(ClickHouseModule)]) });
        var tables = brain.SiloServices.GetRequiredService<TableService>();
        var id = $"{ClickHouseNames.TableIdPrefix}{Guid.NewGuid():N}";
        var neuron = new NeuronId(ClickHouseNames.TableType, id);
        await tables.RegisterAsync(neuron, TestContext.Current.CancellationToken);
        var command = new CreateQueryTableCommand(CommandId.New(), new CreateQueryTable("All companies", "SELECT * FROM companies_current"));
        var table = brain.Grains.GetGrain<IClickHouseTable>(neuron.ToGrainId());
        await table.CreateFromQuery(command, TestContext.Current.CancellationToken);
        await ReactionWait.UntilAsync(async () => (await table.ReadOperation(new ReadTableOperation(command.Id)))?.Status == "applied", TestContext.Current.CancellationToken);

        using var model = new ScriptedChatClient();
        model.CallTool("read_table", JsonSerializer.Serialize(new { id, offset = 0, limit = 5 }));
        model.Say("Twelve companies.");
        await using var app = await StartAsync(brain, model);
        using var client = app.GetTestClient();
        var read = ToolResult(await RunAsync(client, $"What is in table {id}?"));
        Assert.Equal("table", read.GetProperty("kind").GetString());
        Assert.Equal(12, read.GetProperty("totalRows").GetInt32());
        Assert.Equal(5, read.GetProperty("rows").GetArrayLength());
        Assert.Equal("employee_count", read.GetProperty("columns")[7].GetProperty("id").GetString());

        model.CallTool("update_table_view", JsonSerializer.Serialize(new
        {
            id,
            view = new
            {
                expectedRevision = read.GetProperty("revision").GetInt64(),
                filters = new[] { new { columnId = "employee_count", @operator = "gte", value = 50 } },
                sort = new { columnId = "employee_count", descending = true },
                visibleColumns = new[] { "name", "employee_count" },
            },
        }));
        model.Say("Five companies have at least 50 employees.");
        var filtered = ToolResult(await RunAsync(client, "From these, which have more than 50 employees?"));
        Assert.Equal(5, filtered.GetProperty("filteredRows").GetInt32());
        Assert.Equal(12, filtered.GetProperty("totalRows").GetInt32());
        Assert.Equal("gte", filtered.GetProperty("filters")[0].GetProperty("operator").GetString());
        Assert.Equal(310, filtered.GetProperty("rows")[0].GetProperty("cells")[7].GetInt32());

        var page = await client.GetFromJsonAsync<JsonElement>($"/ui/tables/{id}?offset=2&limit=2", TestContext.Current.CancellationToken);
        Assert.Equal(2, page.GetProperty("rows").GetArrayLength());
        Assert.Equal(2, page.GetProperty("offset").GetInt32());
        var listed = await client.GetFromJsonAsync<JsonElement>("/ui/tables", TestContext.Current.CancellationToken);
        Assert.Contains(listed.EnumerateArray(), table => table.GetProperty("id").GetString() == id);
        using var stale = await client.PutAsJsonAsync($"/ui/tables/{id}/view", new
        {
            expectedRevision = 1,
            filters = Array.Empty<object>(),
            sort = (object?)null,
            visibleColumns = new[] { "name" },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(BrainSimulation brain, IChatClient model)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
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
            threadId = Guid.NewGuid().ToString(),
            runId = Guid.NewGuid().ToString(),
            messages = new[] { new { id = Guid.NewGuid().ToString(), role = "user", content = text } },
            tools = Array.Empty<object>(),
            context = Array.Empty<object>(),
            state = new { },
            forwardedProps = new { },
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
