using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Interactions;
using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class CodingNativeToolFacts
{
    private static async Task<(BrainSimulation Brain, NativeTools Tools)> StartAsync()
    {
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(CodingModule)]),
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects));
                silo.Services.AddSingleton<IUntrustedContentScreen, ScriptedContentScreen>();
            },
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = "E:/fixture/Fixture.slnx" },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
        return (brain, brain.SiloServices.GetRequiredService<NativeTools>());
    }

    private static async Task<JsonElement> InvokeAsync(NativeTools tools, string name, Dictionary<string, object?> arguments)
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools.Resolve([name])));
        var result = await tool.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    [Fact]
    public async Task The_four_tools_resolve()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        Assert.Equal(4, tools.Resolve(["code_find_symbols", "code_references", "code_diagnostics", "code_map"]).Count());
    }

    [Fact]
    public async Task Find_symbols_returns_the_envelope()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        var result = await InvokeAsync(tools, "code_find_symbols", new() { ["query"] = "Greeter", ["limit"] = 10 });
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal("T:Alpha.Greeter", result.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task References_with_a_bad_id_return_advice()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        var result = await InvokeAsync(tools, "code_references", new() { ["symbolId"] = "T:Nope" });
        Assert.Contains("find-symbols", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_report_the_broken_file()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        var result = await InvokeAsync(tools, "code_diagnostics", new() { ["path"] = FixtureSolutions.BrokenPath });
        Assert.Equal(1, result.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Map_is_a_graph_result_the_shell_can_open()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        var result = await InvokeAsync(tools, "code_map", new() { ["title"] = "Fixture" });
        Assert.Equal("graph", result.GetProperty("kind").GetString());
        var id = result.GetProperty("id").GetString();
        Assert.StartsWith("map-", id, StringComparison.Ordinal);
        Assert.Equal(12, id!.Length);
        Assert.Equal(id, result.GetProperty("name").GetString());
        var again = await InvokeAsync(tools, "code_map", new() { ["title"] = "Fixture" });
        Assert.Equal(id, again.GetProperty("id").GetString());
        var nodes = result.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Equal(2, nodes.Length);
        Assert.All(nodes, node => Assert.Equal("module", node.GetProperty("kind").GetString()));
        var edge = Assert.Single(result.GetProperty("edges").EnumerateArray());
        Assert.Equal("Beta", edge.GetProperty("sourceId").GetString());
        Assert.Equal("Alpha", edge.GetProperty("targetId").GetString());
    }
}
