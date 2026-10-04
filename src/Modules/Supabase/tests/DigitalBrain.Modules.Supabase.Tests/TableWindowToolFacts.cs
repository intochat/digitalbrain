using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.Flutter;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Windows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Supabase.Tests;

public sealed class TableWindowToolFacts
{
    [Theory]
    [InlineData("table_read")]
    [InlineData("table_refine")]
    public async Task StorageIdsRequireOpeningAWindowAndDoNotMeanTheDatabaseTableIsMissing(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>().WithModule<SupabaseModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ISupabaseProvider>(new FakeSupabaseProvider())).StartAsync(ct);
        var tools = brain.SiloServices.GetServices<IAgentToolFactory>().SelectMany(factory => factory.Create(() => new("workspace", "run", "call")));
        var tool = tools.Single(tool => tool.Name == name);
        var result = JsonSerializer.SerializeToElement(await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        { ["tableId"] = "workspace/packages/intochat/customer-researcher/table" }), ct));
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("table_window_required", result.GetProperty("code").GetString());
        Assert.Contains("discover_capabilities", result.GetProperty("message").GetString());
        Assert.DoesNotContain("was not found", result.GetProperty("message").GetString());

        var opened = await new LiveTableWindows(brain).OpenAsync("workspace", "run", "open", "Customers", "select id from people", ct);
        var retried = JsonSerializer.SerializeToElement(await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        { ["tableId"] = opened.WindowId }), ct));
        Assert.False(retried.TryGetProperty("isError", out _));
    }
}
