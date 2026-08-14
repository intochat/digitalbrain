namespace Brain.Abstractions.Graph;

// A slot is a stable declarative position in a source module's topology. It is
// deliberately opaque: it is neither an adapter-facing graph name nor provider data.
public readonly record struct WiringSlotId
{
    public WiringSlotId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum SynapseRevisionStatus
{
    Live,
    Retired,
}

public enum GraphReason
{
    ManualRetire,
}
