using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Map;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class MapHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        await brain.Get<IMap>("hq").Set(47.6, -122.3, 10, []);
        var state = await brain.HttpClient.GetFromJsonAsync<MapState>("/ui/maps/hq", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal(10, state!.Zoom);
    }
}
