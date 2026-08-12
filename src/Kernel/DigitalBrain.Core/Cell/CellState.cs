using DigitalBrain.Abstractions;
namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.cell-state")]
public sealed record CellState(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Instance,
    [property: Id(2)] string Display,
    [property: Id(3)] double? Value,
    [property: Id(4)] string Phase,
    [property: Id(5)] double Accumulator,
    [property: Id(6)] string? PendingOp,
    [property: Id(7)] bool FreshEntry)
{
    public static CellState Fresh(string kind, string instance)
        => new(kind, instance, "0", 0, "idle", 0, null, true);
}
