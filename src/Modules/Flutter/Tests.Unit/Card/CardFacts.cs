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
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(FlutterModule))] }, ct);
        await brain.Get<ICard>("hero").Set("Title", "Body", [new UiChildRef("button", "go")]);
        Assert.Equal("Title", (await brain.Get<ICard>("hero").Read()).Title);
    }
}
