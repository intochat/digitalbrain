namespace DigitalBrain.Abstractions.Cell;

[GenerateSerializer]
[Alias("db.cell-reset")]
public sealed record CellReset(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<CellSnapshot>;

