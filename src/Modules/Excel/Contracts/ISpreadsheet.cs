using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Excel;

[Alias("sheet")]
public interface ISpreadsheet : INeuron
{
    /// <summary>Applies a replacement grid or a cell edit; the receipt is the version it was accepted against, and the reaction assigns the next one.</summary>
    [Alias("apply")]
    Task<Accepted<SheetVersion>> Apply(ApplySheetEdit command);

    /// <summary>Reads the sheet's current grid.</summary>
    [ReadOnly]
    [Alias("read")]
    Task<ExcelState> Read();

    /// <summary>Reads a range with its column headers.</summary>
    [ReadOnly]
    [Alias("range")]
    Task<SheetRange> ReadRange(ReadRange query);
}
