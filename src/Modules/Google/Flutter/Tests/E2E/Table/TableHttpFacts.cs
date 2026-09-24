using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Table;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Table;

public sealed class TableHttpFacts
{
    [Fact]
    public async Task GetMatchesReplace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<ITable>(UiScope.Key("workspace-a", "grid")).Replace("T", [new TableColumn("c", "C")], [["1"]]);
        var state = await brain.HttpClient.GetFromJsonAsync<TableState>("/workspaces/workspace-a/ui/tables/grid", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("T", state!.Title);
    }
}