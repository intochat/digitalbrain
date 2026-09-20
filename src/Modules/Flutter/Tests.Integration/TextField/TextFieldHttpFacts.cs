using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TextFieldHttpFacts
{
    [Fact]
    public async Task GetMatchesValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        var field = brain.Get<ITextField>("name");
        await field.Configure("Name", "text");
        await field.SetValue("Ada");
        var state = await brain.HttpClient.GetFromJsonAsync<TextFieldState>("/ui/textfields/name", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("Ada", state!.Value);
    }
}
