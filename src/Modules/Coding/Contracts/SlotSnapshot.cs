namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-snapshot")]
public sealed record SlotSnapshot(
    [property: Id(0)] string Slot,
    [property: Id(1)] SlotPhase Phase,
    [property: Id(2)] string? Generation,
    [property: Id(3)] string? ArtifactsPath,
    [property: Id(4)] bool Healthy,
    // A silo knows whether it itself holds the lease, never who else does, so reading the other slot's
    // neuron through this one always answers false.
    [property: Id(5)] bool HoldsLease,
    [property: Id(6)] IReadOnlyList<DiagnosticHit> Errors,
    [property: Id(7)] string? Detail,
    // True when the landing this slot was built for touched no [GenerateSerializer] type, so promoting the
    // previous slot back is still allowed: Orleans versions interfaces, not state (R5.3).
    [property: Id(8)] bool RollbackAllowed,
    [property: Id(9)] int Revision);
