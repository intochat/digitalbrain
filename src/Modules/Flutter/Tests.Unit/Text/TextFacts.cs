using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TextFacts
{
    [Fact]
    public async Task SetWritesMarkdown()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IText>("about").Set("hello");
        Assert.Equal("hello", (await brain.Get<IText>("about").Read()).Markdown);
    }
}
