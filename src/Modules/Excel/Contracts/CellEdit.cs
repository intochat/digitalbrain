namespace DigitalBrain.Excel;

[GenerateSerializer]
[Alias("excel.cell-edit")]
public sealed record CellEdit(
    [property: Id(0)] int Row,
    [property: Id(1)] int Column,
    [property: Id(2)] string Value);
