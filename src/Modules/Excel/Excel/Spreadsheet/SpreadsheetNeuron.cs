using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Excel.Spreadsheet.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Excel.Spreadsheet;

[GrainType("excel.spreadsheet")]
internal sealed class SpreadsheetNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SheetState> state)
    : Neuron, ISpreadsheet
{
    public async Task<SheetVersion> Apply(ApplySheetEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Replace is null && edit.Cell is null)
        {
            throw new ArgumentException("Provide a replacement grid or a cell edit.", nameof(edit));
        }

        if (edit.Replace is not null && edit.Cell is not null)
        {
            throw new ArgumentException("Provide only one of a replacement grid and a cell edit.", nameof(edit));
        }

        if (edit.Cell is { } cell)
        {
            if (cell.Row < 0 || cell.Row >= SheetGrid.MaxRows)
            {
                throw new ArgumentOutOfRangeException(nameof(edit), "row must be within 64 rows");
            }

            if (cell.Column < 0 || cell.Column >= SheetGrid.MaxColumns)
            {
                throw new ArgumentOutOfRangeException(nameof(edit), "column must be within 32 columns");
            }
        }

        var grid = edit.Replace is { } replacement ? SheetGrid.Normalize(replacement) : SheetGrid.WithCell(Grid, edit.Cell!);
        var version = Version + 1;
        state.State = new SheetState(grid, version);
        await state.WriteStateAsync();
        await PublishAsync(new SheetChanged(this.GetPrimaryKeyString(), grid.Title, version));
        return new SheetVersion(version);
    }

    [ReadOnly]
    public Task<ExcelState> Read() => Task.FromResult(Grid);

    [ReadOnly]
    public Task<SheetRange> ReadRange(ReadRange query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Row, nameof(query.Row));
        ArgumentOutOfRangeException.ThrowIfNegative(query.Column, nameof(query.Column));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.RowCount, nameof(query.RowCount));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.ColumnCount, nameof(query.ColumnCount));

        var grid = Grid;
        if (query.Row >= grid.Rows.Count || query.Column >= grid.Columns.Count)
        {
            return Task.FromResult(new SheetRange([], []));
        }

        return Task.FromResult(new SheetRange(
            grid.Columns.Skip(query.Column).Take(query.ColumnCount).ToArray(),
            grid.Rows.Skip(query.Row).Take(query.RowCount)
                .Select(row => new ExcelRow(row.Cells.Skip(query.Column).Take(query.ColumnCount).ToArray())).ToArray()));
    }

    private ExcelState Grid => state.State?.Grid ?? SheetGrid.Empty;
    private long Version => state.State?.Version ?? 0;
}