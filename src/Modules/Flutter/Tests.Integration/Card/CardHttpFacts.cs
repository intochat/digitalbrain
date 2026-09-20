using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CardHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        await brain.Get<ICard>("hero").Set("Title", "Body", []);
        var state = await brain.HttpClient.GetFromJsonAsync<CardState>("/ui/cards/hero", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Title", state!.Title);
    }
}
