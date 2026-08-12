namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.cell-snapshot")]
public sealed record CellSnapshot(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Instance,
    [property: Id(2)] string Display,
    [property: Id(3)] double? Value,
    [property: Id(4)] string Phase) : Synapse;

