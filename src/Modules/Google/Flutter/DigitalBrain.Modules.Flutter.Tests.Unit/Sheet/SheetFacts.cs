using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Sheet;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Sheet;

public sealed class SheetFacts
{
    [Fact]
    public async Task SetWritesTitleAndCells()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<ISheet>("budget").Set("Budget", [new SheetCell(0, 0, "100")]);
        var state = await brain.Get<ISheet>("budget").Read();
        Assert.Equal("Budget", state.Title);
        Assert.Equal(new SheetCell(0, 0, "100"), Assert.Single(state.Cells));
    }
}
