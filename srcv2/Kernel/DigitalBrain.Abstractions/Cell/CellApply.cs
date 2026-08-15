namespace DigitalBrain.Abstractions.Cell;

// Directed input into a cell instance. Key is a kind-defined token
// (calculator: digits, operators, "=", "C", "CE", "BS").
[GenerateSerializer]
[Alias("db.cell-apply")]
public sealed record CellApply(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Key) : RequestSynapse<CellSnapshot>;

