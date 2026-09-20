using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Video;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class VideoHttpFacts
{
    [Fact]
    public async Task GetMatchesLoad()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.WithoutHost())
            .StartAsync(ct);
        await brain.Get<IVideo>("intro").Load("https://example.com/a.mp4", 12);
        var state = await brain.HttpClient.GetFromJsonAsync<VideoState>("/ui/videos/intro", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("https://example.com/a.mp4", state!.Url);
    }
}
