using System.Text.Json;


namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.snapshot")]
public sealed record BehaviorSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] long Version,
    [property: Id(2)] bool Enabled,
    [property: Id(3)] BehaviorDefinition? Definition,
    [property: Id(4)] IReadOnlyList<BehaviorRevision> Versions,
    [property: Id(5)] IReadOnlyList<string> Runs,
    [property: Id(6)] IReadOnlyList<BehaviorReceipt> Receipts);
