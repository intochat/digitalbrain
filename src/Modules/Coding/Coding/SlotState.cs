namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-state")]
internal sealed record SlotState(
    [property: Id(0)] SlotPhase Phase,
    [property: Id(1)] string? Generation,
    [property: Id(2)] string? ArtifactsPath,
    [property: Id(3)] IReadOnlyList<DiagnosticHit> Errors,
    [property: Id(4)] string? Detail,
    [property: Id(5)] bool RollbackAllowed,
    [property: Id(6)] bool Healthy,
    [property: Id(7)] int Revision)
{
    public static readonly SlotState Idle = new(SlotPhase.Idle, null, null, [], null, true, false, 0);
}
