using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Image;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Image;

public sealed class ImageHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<IImage>("logo").Set("https://example.com/a.png", "image/png", "logo");
        var state = await brain.HttpClient.GetFromJsonAsync<ImageState>("/ui/images/logo", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("image/png", state!.MediaType);
    }
}
