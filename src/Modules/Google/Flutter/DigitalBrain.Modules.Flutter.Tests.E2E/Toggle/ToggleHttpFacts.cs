using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Toggle;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Toggle;

public sealed class ToggleHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<IToggle>(UiScope.Key(BrainScope.Create("owner", "workspace-a").Id, "dark")).Set("Dark", true);
        var state = await brain.HttpClient.GetFromJsonAsync<ToggleState>("/brains/workspace-a/ui/toggles/dark", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.True(state!.On);
    }
}
