namespace DigitalBrain.Excel;

[GenerateSerializer]
[Alias("excel.sheet-range")]
public sealed record SheetRange(
    [property: Id(0)] IReadOnlyList<string> Columns,
    [property: Id(1)] IReadOnlyList<ExcelRow> Rows);
