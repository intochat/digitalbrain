using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Video;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class VideoFacts
{
    [Fact]
    public async Task LoadThenPlay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new FlutterModule()] }, ct);
        var video = brain.Get<IVideo>("intro");
        await video.Load("https://example.com/a.mp4", 12);
        await video.Play();
        Assert.True((await video.Read()).Playing);
    }
}
