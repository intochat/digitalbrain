using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Toggle;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Toggle;

[Collection(FlutterHostCollection.Name)]
public sealed class ToggleHttpFacts(FlutterHostFixture host)
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = host.Brain;
        var ws = host.Workspace();
        await brain.Get<IToggle>(UiScope.Key(BrainScope.Create("owner", ws).Id, "dark")).Set("Dark", true);
        var state = await brain.HttpClient.GetFromJsonAsync<ToggleState>($"/brains/{ws}/ui/toggles/dark", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.True(state!.On);
    }
}
