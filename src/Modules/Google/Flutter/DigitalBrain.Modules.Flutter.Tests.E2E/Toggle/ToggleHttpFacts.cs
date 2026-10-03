using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Toggle;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Toggle;

[Collection(FlutterBackendCollection.Name)]
public sealed class ToggleHttpFacts(FlutterBackendFixture host)
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        await brain.Get<IToggle>(UiScope.Key(BrainScope.Create("owner", brain.WorkspaceId).Id, "dark")).Set("Dark", true);
        var state = await brain.HttpClient.GetFromJsonAsync<ToggleState>($"/brains/{brain.WorkspaceId}/ui/toggles/dark", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.True(state!.On);
    }
}
