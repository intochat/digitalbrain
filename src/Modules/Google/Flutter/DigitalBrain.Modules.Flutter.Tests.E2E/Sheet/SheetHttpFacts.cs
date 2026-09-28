using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Sheet;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Sheet;

public sealed class SheetHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<ISheet>(UiScope.Key("workspace-a", "budget")).Set("Budget", [new SheetCell(0, 0, "100")]);
        var state = await brain.HttpClient.GetFromJsonAsync<SheetState>("/workspaces/workspace-a/ui/sheets/budget", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Budget", state!.Title);
    }
}