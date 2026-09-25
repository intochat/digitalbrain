using System.Text.Json;
using DigitalBrain.AI.Agents;
using IntoChat.Agent;
using Microsoft.Extensions.AI;
using Xunit;

namespace IntoChat.Tests.Unit;

public sealed class WorkspaceChartToolFacts
{
    [Fact]
    public async Task RenderChartReturnsWorkspaceArtifactWithPiePoints()
    {
        var tool = Assert.Single(new WorkspaceChartTools().Create(() => new AgentToolContext("workspace", "run", "call")));
        var output = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["title"] = "Companies by location",
            ["chartKind"] = "pie",
            ["labels"] = new[] { "London", "Bristol" },
            ["values"] = new double[] { 8, 3 },
        }, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(output));
        Assert.Equal("chart", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("pie", json.RootElement.GetProperty("chartKind").GetString());
        Assert.Equal("London", json.RootElement.GetProperty("points")[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task PieChartKeepsLargestElevenAndGroupsTheRest()
    {
        var tool = Assert.Single(new WorkspaceChartTools().Create(() => new AgentToolContext("workspace", "run", "call")));
        var labels = Enumerable.Range(1, 20).Select(index => $"Town {index}").ToArray();
        var values = Enumerable.Range(1, 20).Select(index => (double)index).ToArray();
        var output = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["title"] = "Companies by location", ["chartKind"] = "pie",
            ["labels"] = labels, ["values"] = values,
        }, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(output));
        var points = json.RootElement.GetProperty("points");
        Assert.Equal(12, points.GetArrayLength());
        Assert.Equal("Town 20", points[0].GetProperty("label").GetString());
        Assert.Equal("Other", points[11].GetProperty("label").GetString());
        Assert.Equal(45, points[11].GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task RejectsMismatchedOrNegativePieData()
    {
        var tool = Assert.Single(new WorkspaceChartTools().Create(() => new AgentToolContext("workspace", "run", "call")));
        var output = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["title"] = "Invalid", ["chartKind"] = "pie",
            ["labels"] = new[] { "London" }, ["values"] = new double[] { -1 },
        }, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(output));
        Assert.True(json.RootElement.GetProperty("isError").GetBoolean());
    }
}
