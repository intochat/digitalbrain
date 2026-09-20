using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Person;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class PersonHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        await brain.Get<IPerson>("ada").Set("Ada", "https://example.com/ada.png");
        var state = await brain.HttpClient.GetFromJsonAsync<PersonState>("/ui/people/ada", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Ada", state!.DisplayName);
    }
}
