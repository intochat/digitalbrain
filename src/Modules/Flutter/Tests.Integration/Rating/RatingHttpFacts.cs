using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Rating;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class RatingHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        await brain.Get<IRating>("stars").Set(5, 4);
        var state = await brain.HttpClient.GetFromJsonAsync<RatingState>("/ui/ratings/stars", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal(4, state!.Value);
    }
}
