namespace DigitalBrain.Excel;

[GenerateSerializer]
[Alias("excel.applying-body")]
public sealed record ApplyingBody(
    [property: Id(0)] ExcelState? Replace,
    [property: Id(1)] CellEdit? Cell);
