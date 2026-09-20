using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CardFacts
{
    [Fact]
    public async Task SetWritesTitle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new() { Modules = [new FlutterModule()] }, ct);
        await brain.Get<ICard>("hero").Set("Title", "Body", [new UiChildRef("button", "go")]);
        Assert.Equal("Title", (await brain.Get<ICard>("hero").Read()).Title);
    }
}
