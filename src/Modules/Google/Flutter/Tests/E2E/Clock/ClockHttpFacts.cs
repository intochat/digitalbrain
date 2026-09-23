using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Clock;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Clock;

public sealed class ClockHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<IClock>(UiScope.Key("workspace-a", "tea")).Set("Tea", DateTimeOffset.UnixEpoch);
        var state = await brain.HttpClient.GetFromJsonAsync<ClockState>("/workspaces/workspace-a/ui/clocks/tea", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Tea", state!.Label);
    }
}