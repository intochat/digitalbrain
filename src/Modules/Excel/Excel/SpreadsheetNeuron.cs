using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Excel;

[GrainType("sheet")]
internal sealed class SpreadsheetNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SheetState> state)
    : Neuron<SheetState>(runtime, state), ISpreadsheet
{
    public Task<Accepted<SheetVersion>> Apply(ApplySheetEdit command) => ExecuteCommandAsync(
        Descriptor("apply"), command, ExcelJson.Default.ApplySheetEdit, ExcelJson.Default.AcceptedSheetVersion, arguments =>
        {
            if (arguments.Replace is null && arguments.Cell is null)
            {
                throw new CommandRejectedException(arguments.Id, "no edit provided", "Provide a replacement grid or a cell edit.");
            }

            if (arguments.Replace is not null && arguments.Cell is not null)
            {
                throw new CommandRejectedException(arguments.Id, "both edits provided", "Provide only one of a replacement grid and a cell edit.");
            }

            if (arguments.Cell is { } cell)
            {
                if (cell.Row < 0 || cell.Row >= SheetGrid.MaxRows)
                {
                    throw new CommandRejectedException(arguments.Id, "row must be within 64 rows", "Provide a row index from 0 through 63.");
                }

                if (cell.Column < 0 || cell.Column >= SheetGrid.MaxColumns)
                {
                    throw new CommandRejectedException(arguments.Id, "column must be within 32 columns", "Provide a column index from 0 through 31.");
                }
            }

            var body = new ApplyingBody(
                arguments.Replace is { } replacement ? SheetGrid.Normalize(replacement) : null,
                arguments.Cell);
            var work = Schedule(Signal.Create(ExcelSignals.Applying, JsonSerializer.Serialize(body, ExcelJson.Default.ApplyingBody)));
            return new Accepted<SheetVersion>(new SheetVersion(State?.Version ?? 0), work);
        });

    private ExcelState Grid => State?.Grid ?? SheetGrid.Empty;

    public Task<ExcelState> Read() => Task.FromResult(Grid);

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

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != ExcelSignals.Applying)
        {
            return;
        }

        var body = JsonSerializer.Deserialize(delivery.Signal.Body, ExcelJson.Default.ApplyingBody)!;
        var grid = body.Replace is { } replacement ? replacement : SheetGrid.Normalize(SheetGrid.WithCell(Grid, body.Cell!));
        var version = (State?.Version ?? 0) + 1;
        await SaveAsync(new SheetState(grid, version), cancellationToken).ConfigureAwait(true);
        var changed = new SheetChangedBody(Id.Name, grid.Title, version);
        await FireAsync(Signal.Create(ExcelSignals.SheetChanged, JsonSerializer.Serialize(changed, ExcelJson.Default.SheetChangedBody)),
            to: null, delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
    }
}
