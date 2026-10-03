using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Image;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Image;

public sealed class ImageFacts
{
    [Fact]
    public async Task SetWritesUrlMediaTypeAndPrompt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IImage>("logo").Set("https://example.com/a.png", "image/png", "logo");
        var state = await brain.Get<IImage>("logo").Read();
        Assert.Equal("https://example.com/a.png", state.Url);
        Assert.Equal("image/png", state.MediaType);
        Assert.Equal("logo", state.Prompt);
    }
}
