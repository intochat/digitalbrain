using DigitalBrain.Supabase;
using DigitalBrain.Testing;
using DigitalBrain.Flutter;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DigitalBrain.AI;
using IntoChat;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests;

public sealed class SupabaseAgentFacts
{
    [Fact]
    public async Task Conversational_agent_queries_and_creates_a_live_Supabase_table()
    {
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(FlutterModule), typeof(SupabaseModule), typeof(DigitalBrain.ClickHouse.ClickHouseModule)]),
            Configuration = new Dictionary<string, string?> { ["DigitalBrain:Fakes:Enabled"] = "true" },
        });
        using var model = new ScriptedChatClient();
        model.CallTool("supabase_query", """{"sql":"SELECT 1 AS value"}""");
        model.Say("One row.");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(brain.SiloServices.GetRequiredService<TableService>());
        builder.Services.AddSingleton(new NativeTools(brain.SiloServices.GetServices<INativeToolContributor>(), brain.SiloServices));
        builder.Services.AddSingleton(new ChatClientBuilder(model).UseFunctionInvocation().Build());
        builder.AddConversationalAgent();
        await using var app = builder.Build();
        app.MapConversationalAgent();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        var query = TableAgentFacts.ToolResult(await TableAgentFacts.RunAsync(client, "Query Supabase"));
        Assert.Equal(1, query.GetProperty("rowCount").GetInt32());

        model.CallTool("show_supabase_query_table", """{"title":"Supabase results","sql":"SELECT 1 AS value"}""");
        model.Say("Here is the table.");
        var created = TableAgentFacts.ToolResult(await TableAgentFacts.RunAsync(client, "Show the Supabase results"));
        Assert.Equal("table", created.GetProperty("kind").GetString());
        var id = created.GetProperty("id").GetString()!;
        Assert.StartsWith("sbtable-", id, StringComparison.Ordinal);
        var tables = brain.SiloServices.GetRequiredService<TableService>();
        Assert.Equal(1, (await tables.ReadAsync(id, cancellationToken: TestContext.Current.CancellationToken)).TotalRows);

        // Editing a view changes the saved view only; the database rows remain untouched.
        model.CallTool("update_table_view", System.Text.Json.JsonSerializer.Serialize(new
        {
            id,
            view = new { expectedRevision = 1, filters = new[] { new { columnId = "value", @operator = "gt", value = 1 } }, visibleColumns = new[] { "value" } },
        }));
        model.Say("No matching rows.");
        var filtered = TableAgentFacts.ToolResult(await TableAgentFacts.RunAsync(client, "Only show values greater than one"));
        Assert.Equal(0, filtered.GetProperty("filteredRows").GetInt32());
        Assert.Equal(1, filtered.GetProperty("totalRows").GetInt32());
    }
}
