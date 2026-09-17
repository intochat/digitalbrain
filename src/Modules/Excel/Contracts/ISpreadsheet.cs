using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Excel;

[Alias(ExcelVocabulary.SpreadsheetType)]
public interface ISpreadsheet : INeuron
{
    /// <summary>Applies a replacement grid or a cell edit; the receipt is the version it was accepted against, and the reaction assigns the next one.</summary>
    [Alias("apply")]
    [NeuronTool]
    Task<Accepted<SheetVersion>> Apply(ApplySheetEdit command);

    /// <summary>Reads the sheet's current grid.</summary>
    [ReadOnly]
    [Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<ExcelState> Read();

    /// <summary>Reads a range with its column headers.</summary>
    [ReadOnly]
    [Alias("range")]
    [NeuronTool(IsReadOnly = true)]
    Task<SheetRange> ReadRange(ReadRange query);
}
