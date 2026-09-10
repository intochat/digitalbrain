namespace DigitalBrain.Excel;

[GenerateSerializer]
[Alias("excel.applying-body")]
public sealed record ApplyingBody(
    [property: Id(0)] ExcelState Grid,
    [property: Id(1)] long Version);
