using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Sheet;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SheetFacts
{
    [Fact]
    public async Task SetWritesTitle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new() { Modules = [new FlutterModule()] }, ct);
        await brain.Get<ISheet>("budget").Set("Budget", [new SheetCell(0, 0, "100")]);
        Assert.Equal("Budget", (await brain.Get<ISheet>("budget").Read()).Title);
    }
}
