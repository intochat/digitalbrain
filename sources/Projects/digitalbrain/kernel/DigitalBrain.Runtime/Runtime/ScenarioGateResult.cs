namespace DigitalBrain.Runtime.Runtime;

// L6 (docs/v3/VISION.md §L6) refusal as a typed result so the bus stays
// exception-free — CLAUDE.md says "emit a failure synapse, don't throw
// across the cortex." The grain returns this from ConfigureAsync; callers
// that need an early-fail path branch on CanActivate.
[GenerateSerializer]
public sealed record ScenarioGateResult(
    [property: Id(0)] bool CanActivate,
    [property: Id(1)] string Fqn,
    [property: Id(2)] string Reason)
{
    public static ScenarioGateResult Activated(string fqn) =>
        new(true, fqn, $"'{fqn}' — all scenarios green.");

    public static ScenarioGateResult Refused(string fqn, string reason) =>
        new(false, fqn, reason);
}
