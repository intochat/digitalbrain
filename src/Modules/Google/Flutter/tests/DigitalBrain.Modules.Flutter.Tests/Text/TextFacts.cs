using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Text;

public sealed class TextFacts
{
    [Fact]
    public async Task SetWritesMarkdown()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IText>("about").Set("hello");
        Assert.Equal("hello", (await brain.Get<IText>("about").Read()).Markdown);
    }
}
