namespace DigitalBrain.Abstractions.Slots;

// Two silos of one service share grain storage and reminder rows, so only the lease holder may change
// anything. This refusal is transient and is never journaled: after a promotion the same command works.
[GenerateSerializer]
[Alias("db.v3.standby-slot")]
public sealed class StandbySlotException(string slot)
    : InvalidOperationException($"standby slot: silo '{slot}' does not hold the active lease")
{
    [Id(0)] public string Slot { get; } = slot;
}
