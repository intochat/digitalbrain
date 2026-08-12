namespace DigitalBrain.Abstractions;

// Directed input into a cell instance. Key is a kind-defined token
// (calculator: digits, operators, "=", "C", "CE", "BS").
[GenerateSerializer]
[Alias("db.cell-apply")]
public sealed record CellApply(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Key) : RequestSynapse<CellSnapshot>;

[GenerateSerializer]
[Alias("db.cell-reset")]
public sealed record CellReset(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<CellSnapshot>;

[GenerateSerializer]
[Alias("db.cell-snapshot")]
public sealed record CellSnapshot(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Instance,
    [property: Id(2)] string Display,
    [property: Id(3)] double? Value,
    [property: Id(4)] string Phase) : Synapse;

// Carrier for kind-declared facts that are not compiled CLR synapses.
// Effective wire alias for routing is Kind (e.g. "calc.result"), not the carrier alias.
[GenerateSerializer]
[Alias("db.datum")]
public sealed record Datum(
    [property: Id(0)] string Kind,
    [property: Id(1)] Dictionary<string, string> Fields) : Synapse;
