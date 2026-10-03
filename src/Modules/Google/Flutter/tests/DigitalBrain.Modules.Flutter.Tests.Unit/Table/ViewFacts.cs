using DigitalBrain.Contracts.Data;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chart;
using DigitalBrain.Flutter.Table;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Table;

public sealed class ViewFacts
{
    [Fact]
    public async Task ABoundTableAndChartReadTheSourceWithoutReplacingItsRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var rows = brain.Get<IStoredRows>("people");
        await rows.Replace(
            new RowSchema([new("country", "text"), new("n", "number")]),
            [new(["us", "2"]), new(["uk", "1"])]);
        var table = brain.Get<ITable>("grid");
        var chart = brain.Get<IChart>("pie");
        var query = new RowQuery { Sort = [new("country", false)] };

        await table.Bind(rows.GetGrainId().ToString(), query);
        await chart.Bind(rows.GetGrainId().ToString(), query);
        var read = await table.Read();
        var plotted = await chart.Read();
        await rows.Replace(new RowSchema([new("country", "text"), new("n", "number")]), [new(["fr", "4"])]);

        Assert.Equal(["uk", "us"], read.Rows.Select(row => row[0]).ToArray());
        Assert.Equal("uk", plotted.Points[0].Label);
        Assert.Equal(1, plotted.Points[0].Value);
        Assert.Equal(["fr"], (await table.Read()).Rows.Select(row => row[0]).ToArray());
    }
}
