namespace DigitalBrain.Units;

/// <summary>A synapse slot has no binding, or its bound connection is not usable.</summary>
/// <param name="alias">The state alias of the synapse.</param>
/// <param name="slot">The slot name.</param>
public sealed class SlotUnboundException(string alias, string slot) : Exception($"slot '{slot}' is not bound for '{alias}'")
{
    /// <summary>The state alias of the synapse.</summary>
    public string Alias { get; } = alias;

    /// <summary>The slot name.</summary>
    public string Slot { get; } = slot;
}
