namespace DigitalBrain.Excel;

[GenerateSerializer]
[Alias("excel.state")]
internal sealed record SheetState(
    [property: Id(0)] ExcelState Grid,
    [property: Id(1)] long Version);
