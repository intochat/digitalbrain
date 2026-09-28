using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Card;

public sealed class CardHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<ICard>(UiScope.Key("workspace-a", "hero")).Set("Title", "Body", []);
        var state = await brain.HttpClient.GetFromJsonAsync<CardState>("/workspaces/workspace-a/ui/cards/hero", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Title", state!.Title);
    }
}