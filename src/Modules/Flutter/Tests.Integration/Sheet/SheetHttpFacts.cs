using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Sheet;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SheetHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        await brain.Get<ISheet>("budget").Set("Budget", [new SheetCell(0, 0, "100")]);
        var state = await brain.HttpClient.GetFromJsonAsync<SheetState>("/ui/sheets/budget", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Budget", state!.Title);
    }
}
