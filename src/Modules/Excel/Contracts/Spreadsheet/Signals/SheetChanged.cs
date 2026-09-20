using DigitalBrain.Contracts;

namespace DigitalBrain.Excel.Spreadsheet.Signals;

[GenerateSerializer, Alias("excel.sheet-changed")]
public sealed record SheetChanged(
    [property: Id(0)] string Name,
    [property: Id(1)] string Title,
    [property: Id(2)] long Version) : Signal;