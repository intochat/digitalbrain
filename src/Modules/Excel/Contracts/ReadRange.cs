namespace DigitalBrain.Excel;

/// <summary>A query for a rectangular range of cells.</summary>
[GenerateSerializer]
[Alias("excel.read-range")]
public sealed record ReadRange(
    [property: Id(0)] int Row,
    [property: Id(1)] int Column,
    [property: Id(2)] int RowCount,
    [property: Id(3)] int ColumnCount);
