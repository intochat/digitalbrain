using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Excel.Spreadsheet;

[Alias("sheet")]
public interface ISpreadsheet : INeuron
{
    Task<SheetVersion> Apply(ApplySheetEdit edit);

    [ReadOnly]
    Task<ExcelState> Read();

    [ReadOnly]
    Task<SheetRange> ReadRange(ReadRange query);
}