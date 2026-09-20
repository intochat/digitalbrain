namespace DigitalBrain.Excel.Spreadsheet;

[GenerateSerializer]
[Alias("excel.sheet-version")]
public sealed record SheetVersion([property: Id(0)] long Value);