using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TextHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.WithoutHost())
            .StartAsync(ct);
        await brain.Get<IText>("about").Set("hello");
        var state = await brain.HttpClient.GetFromJsonAsync<TextState>("/ui/texts/about", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("hello", state!.Markdown);
    }
}
