using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Person;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Person;

public sealed class PersonHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<IPerson>(UiScope.Key("workspace-a", "ada")).Set("Ada", "https://example.com/ada.png");
        var state = await brain.HttpClient.GetFromJsonAsync<PersonState>("/workspaces/workspace-a/ui/people/ada", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Ada", state!.DisplayName);
    }
}