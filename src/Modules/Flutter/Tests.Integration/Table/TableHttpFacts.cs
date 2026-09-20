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
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.WithoutHost())
            .StartAsync(ct);
        await brain.Get<ITable>("grid").Replace("T", [new TableColumn("c", "C")], [["1"]]);
        var state = await brain.HttpClient.GetFromJsonAsync<TableState>("/ui/tables/grid", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("T", state!.Title);
    }
}
