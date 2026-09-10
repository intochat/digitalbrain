namespace DigitalBrain.Excel;

[GenerateSerializer]
[Alias("excel.sheet-changed-body")]
public sealed record SheetChangedBody(
    [property: Id(0)] string Name,
    [property: Id(1)] string Title,
    [property: Id(2)] long Version);
