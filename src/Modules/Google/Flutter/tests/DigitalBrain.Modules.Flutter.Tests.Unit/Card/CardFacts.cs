using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Card;

public sealed class CardFacts
{
    [Fact]
    public async Task SetWritesTitleBodyAndChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<ICard>("hero").Set("Title", "Body", [new UiChildRef("button", "go")]);
        var state = await brain.Get<ICard>("hero").Read();
        Assert.Equal("Title", state.Title);
        Assert.Equal("Body", state.Body);
        Assert.Equal(new UiChildRef("button", "go"), Assert.Single(state.Children));
    }
}
