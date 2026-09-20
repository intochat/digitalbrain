using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Table;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TableHttpFacts
{
    [Fact]
    public async Task GetMatchesReplace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        await brain.Get<ITable>("grid").Replace("T", [new TableColumn("c", "C")], [["1"]]);
        var state = await brain.HttpClient.GetFromJsonAsync<TableState>("/ui/tables/grid", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("T", state!.Title);
    }
}
