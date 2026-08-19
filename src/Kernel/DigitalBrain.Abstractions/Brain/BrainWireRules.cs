namespace DigitalBrain.Abstractions.Brain;

public static class BrainWireRules
{
    // Call-graph interior neurons: their edges are compiled-in awaited calls, not brain wires,
    // so BrainEntity.Connect's table-walk cycle check cannot see them — wiring one as an
    // endpoint can re-enter an awaited delivery chain and deadlock every hop on it.
    public static readonly HashSet<string> InfrastructureGrainTypes =
    [
        "sessionneuron",
        "surface-boot",
        "chat-turn-worker",
        "grants",
    ];
}
