using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Clock;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ClockHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.WithoutHost())
            .StartAsync(ct);
        await brain.Get<IClock>("tea").Set("Tea", DateTimeOffset.UnixEpoch);
        var state = await brain.HttpClient.GetFromJsonAsync<ClockState>("/ui/clocks/tea", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Tea", state!.Label);
    }
}
