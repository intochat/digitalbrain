using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Image;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ImageFacts
{
    [Fact]
    public async Task SetWritesMediaType()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IImage>("logo").Set("https://example.com/a.png", "image/png", "logo");
        Assert.Equal("image/png", (await brain.Get<IImage>("logo").Read()).MediaType);
    }
}
