using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Table;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TableFacts
{
    [Fact]
    public async Task ReplaceAndSetView()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var table = brain.Get<ITable>("grid");
        await table.Replace("T", [new TableColumn("c", "C")], [["1"]]);
        await table.SetView("c", "1");
        Assert.Equal("c", (await table.Read()).Sort);
    }
}
