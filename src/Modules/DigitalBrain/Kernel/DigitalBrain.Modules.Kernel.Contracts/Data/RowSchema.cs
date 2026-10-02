namespace DigitalBrain.Contracts.Data;

[GenerateSerializer, Alias("data.row-column")]
public sealed record RowColumn(
    [property: Id(0)] string Name,
    [property: Id(1)] string Type);

[GenerateSerializer, Alias("data.row-schema")]
public sealed record RowSchema([property: Id(0)] RowColumn[] Columns);

[GenerateSerializer, Alias("data.row")]
public sealed record Row([property: Id(0)] string[] Values);

[GenerateSerializer, Alias("data.row-page")]
public sealed record RowPage(
    [property: Id(0)] RowColumn[] Columns,
    [property: Id(1)] Row[] Rows,
    [property: Id(2)] bool Truncated);
