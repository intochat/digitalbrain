using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Tabs;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TabsHttpFacts
{
    [Fact]
    public async Task GetMatchesSelect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        await brain.Get<ITabs>("pages").Set(
            [new TabItem("a", "A", new UiChildRef("text", "about"))],
            "a");
        var state = await brain.HttpClient.GetFromJsonAsync<TabsState>("/ui/tabs/pages", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("a", state!.SelectedId);
    }
}
