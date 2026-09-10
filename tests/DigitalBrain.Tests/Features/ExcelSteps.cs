using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Excel;
using Orleans.Runtime;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class ExcelSteps(BrainWorld world)
{
    private Exception? _lastError;
    private Accepted<SheetVersion>? _lastAccepted;

    [When(@"""(.*)"" applies a replacement to sheet ""(.*)"" with blank title and sheet name and ragged rows")]
    public Task ApplyReplacement(string principal, string name, DataTable table)
    {
        // Trailing blank table cells represent omitted cells in the replacement's ragged rows.
        var rows = table.Rows.Select(row => new ExcelRow(row.Values.Reverse()
            .SkipWhile(string.IsNullOrEmpty).Reverse().ToArray())).ToArray();
        return Apply(principal, name, new ApplySheetEdit(CommandId.New(),
            new ExcelState(" ", " ", table.Header.ToArray(), rows), null));
    }

    [When(@"""(.*)"" applies a cell edit to sheet ""(.*)"" at row (\d+) and column (\d+) with value ""(.*)""")]
    public Task ApplyCell(string principal, string name, int row, int column, string value)
        => Apply(principal, name, new ApplySheetEdit(CommandId.New(), null, new CellEdit(row, column, value)));

    [When(@"""(.*)"" tries to apply a cell edit to sheet ""(.*)"" at row (\d+) and column (\d+) with value ""(.*)""")]
    public async Task TryApplyCell(string principal, string name, int row, int column, string value)
    {
        _lastError = null;
        try
        {
            await ApplyCell(principal, name, row, column, value);
        }
        catch (Exception error) when (BrainSteps.Flatten(error) is CommandRejectedException)
        {
            _lastError = error;
        }
    }

    [Then(@"the spreadsheet command is accepted against version (\d+)")]
    public void CommandAccepted(long version)
    {
        Assert.Null(_lastError);
        Assert.NotNull(_lastAccepted);
        Assert.Equal(new SheetVersion(version), _lastAccepted.Receipt);
        Assert.NotEqual(default, _lastAccepted.Work);
    }

    [Then(@"the spreadsheet command fails with ""(.*)""")]
    public void CommandFails(string reason)
    {
        Assert.NotNull(_lastError);
        var error = Assert.IsType<CommandRejectedException>(BrainSteps.Flatten(_lastError));
        Assert.Contains(reason, error.Reason, StringComparison.Ordinal);
    }

    [Then(@"""(.*)"" waits up to (\d+) seconds until sheet ""(.*)"" has title ""(.*)"" and sheet name ""(.*)"" with the grid")]
    public async Task WaitForGrid(string principal, int seconds, string name, string title, string sheetName, DataTable table)
    {
        var columns = table.Header.ToArray();
        var rows = table.Rows.Select(row => row.Values.ToArray()).ToArray();
        var grid = await WaitForSheet(principal, seconds, name, grid =>
            grid.Title == title && grid.SheetName == sheetName && grid.Columns.SequenceEqual(columns)
            && grid.Rows.Count == rows.Length
            && grid.Rows.Select((row, index) => row.Cells.SequenceEqual(rows[index])).All(matches => matches));
        Assert.Equal(title, grid.Title);
        Assert.Equal(sheetName, grid.SheetName);
        Assert.Equal(columns, grid.Columns);
        Assert.Equal(rows.Length, grid.Rows.Count);
        Assert.All(grid.Rows, row => Assert.Equal(columns.Length, row.Cells.Count));
        for (var index = 0; index < rows.Length; index++)
        {
            Assert.Equal(rows[index], grid.Rows[index].Cells);
        }
    }

    [Then(@"""(.*)"" waits up to (\d+) seconds until sheet ""(.*)"" has (\d+) rows and (\d+) columns with value ""(.*)"" at row (\d+) and column (\d+)")]
    public async Task WaitForCell(string principal, int seconds, string name, int rowCount, int columnCount, string value, int row, int column)
    {
        var grid = await WaitForSheet(principal, seconds, name, grid =>
            grid.Rows.Count == rowCount && grid.Columns.Count == columnCount
            && grid.Rows.All(cells => cells.Cells.Count == columnCount) && grid.Rows[row].Cells[column] == value);
        Assert.Equal(rowCount, grid.Rows.Count);
        Assert.Equal(columnCount, grid.Columns.Count);
        Assert.All(grid.Rows, cells => Assert.Equal(columnCount, cells.Cells.Count));
        Assert.Equal(value, grid.Rows[row].Cells[column]);
    }

    [Then(@"""(.*)"" waits up to (\d+) seconds until sheet ""(.*)"" has column headers ""(.*)""")]
    public async Task WaitForHeaders(string principal, int seconds, string name, string headers)
    {
        var expected = headers.Split(',');
        var grid = await WaitForSheet(principal, seconds, name, grid => grid.Columns.SequenceEqual(expected));
        Assert.Equal(expected, grid.Columns);
    }

    [Then(@"""(.*)"" reads 1 row and 1 column from sheet ""(.*)"" at row (\d+) and column (\d+) with header ""(.*)"" and value ""(.*)""")]
    public async Task ReadCellRange(string principal, string name, int row, int column, string header, string value)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        var range = await Spreadsheet(name).ReadRange(new ReadRange(row, column, 1, 1));
        Assert.Equal(header, Assert.Single(range.Columns));
        Assert.Equal(value, Assert.Single(Assert.Single(range.Rows).Cells));
    }

    private async Task<ExcelState> WaitForSheet(string principal, int seconds, string name, Func<ExcelState, bool> satisfied)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var sheet = Spreadsheet(name);
        var grid = await sheet.Read();
        while (!satisfied(grid) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            grid = await sheet.Read();
        }

        return grid;
    }

    private async Task Apply(string principal, string name, ApplySheetEdit command)
    {
        _lastError = null;
        _lastAccepted = null;
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        _lastAccepted = await Spreadsheet(name).Apply(command);
    }

    [When(@"""(.*)"" applies cell edits to sheet ""(.*)"" concurrently, ""(.*)"" at row (\d+) and column (\d+) and ""(.*)"" at row (\d+) and column (\d+)")]
    public async Task ApplyCellsConcurrently(string principal, string name, string firstValue, int firstRow, int firstColumn,
        string secondValue, int secondRow, int secondColumn)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        var sheet = Spreadsheet(name);
        var first = sheet.Apply(new ApplySheetEdit(CommandId.New(), null, new CellEdit(firstRow, firstColumn, firstValue)));
        var second = sheet.Apply(new ApplySheetEdit(CommandId.New(), null, new CellEdit(secondRow, secondColumn, secondValue)));
        await Task.WhenAll(first, second);
    }

    private ISpreadsheet Spreadsheet(string name)
        => world.Brain.Grains.GetGrain<ISpreadsheet>(new NeuronId("sheet", name).ToGrainId());
}
