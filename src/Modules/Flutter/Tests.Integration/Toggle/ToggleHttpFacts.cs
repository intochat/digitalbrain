using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Toggle;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ToggleHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.WithoutHost())
            .StartAsync(ct);
        await brain.Get<IToggle>("dark").Set("Dark", true);
        var state = await brain.HttpClient.GetFromJsonAsync<ToggleState>("/ui/toggles/dark", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.True(state!.On);
    }
}
