using DigitalBrain.Excel;
using DigitalBrain.Excel.Spreadsheet;
using DigitalBrain.Excel.Spreadsheet.Signals;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SpreadsheetFacts
{
    [Fact]
    public async Task CellEditIncrementsTheVersionAndPublishes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(
            new() { Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(ExcelModule))] }, ct);
        var sheet = brain.Get<ISpreadsheet>("book");
        await using var changed = await brain.Observe<SheetChanged>(sheet, ct);
        var version = await sheet.Apply(new(null, new CellEdit(0, 0, "42")));
        Assert.Equal(1, version.Value);
        var published = await changed.NextAsync(ct: ct);
        Assert.Equal("book", published.Name);
        Assert.Equal(1, published.Version);
    }

    [Fact]
    public async Task ReplacementGridReadsBackAndRangeIsSliced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(
            new() { Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(ExcelModule))] }, ct);
        var sheet = brain.Get<ISpreadsheet>("book");
        await sheet.Apply(new(
            new ExcelState("Budget", "Sheet1", ["A", "B"], [new ExcelRow(["1", "2"]), new ExcelRow(["3", "4"])]),
            null));
        var read = await sheet.Read();
        Assert.Equal("Budget", read.Title);
        Assert.Equal(2, read.Rows.Count);
        var range = await sheet.ReadRange(new(1, 1, 1, 1));
        Assert.Equal(["B"], range.Columns);
        Assert.Equal("4", Assert.Single(Assert.Single(range.Rows).Cells));
    }

    [Fact]
    public async Task InvalidEditsAreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(
            new() { Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(ExcelModule))] }, ct);
        var sheet = brain.Get<ISpreadsheet>("book");
        await Assert.ThrowsAsync<ArgumentException>(() => sheet.Apply(new(null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => sheet.Apply(new(
            new ExcelState("x", "y", ["A"], []), new CellEdit(0, 0, "1"))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sheet.Apply(new(null, new CellEdit(64, 0, "1"))));
    }
}