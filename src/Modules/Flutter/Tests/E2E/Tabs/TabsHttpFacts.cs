using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Tabs;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Tabs;

public sealed class TabsHttpFacts
{
    [Fact]
    public async Task GetMatchesSelect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<ITabs>("pages").Set(
            [new TabItem("a", "A", new UiChildRef("text", "about"))],
            "a");
        var state = await brain.HttpClient.GetFromJsonAsync<TabsState>("/ui/tabs/pages", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("a", state!.SelectedId);
    }
}