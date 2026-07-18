namespace DigitalBrain.Runtime.Neurons;

using System;
using Orleans;

[GenerateSerializer]
public readonly record struct SynapseId([property: Id(0)] Guid Value)
{
    public static SynapseId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();

    public static implicit operator Guid(SynapseId id) => id.Value;
    public static implicit operator SynapseId(Guid guid) => new(guid);
}

[GenerateSerializer]
public readonly record struct CorrelationId([property: Id(0)] Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();

    public static implicit operator Guid(CorrelationId id) => id.Value;
    public static implicit operator CorrelationId(Guid guid) => new(guid);
}

[GenerateSerializer]
public readonly record struct CausationId([property: Id(0)] Guid Value)
{
    public override string ToString() => Value.ToString();

    public static implicit operator Guid(CausationId id) => id.Value;
    public static implicit operator CausationId(Guid guid) => new(guid);
    public static implicit operator Guid?(CausationId? id) => id?.Value;
    public static implicit operator CausationId?(Guid? guid) => guid == null ? null : new CausationId(guid.Value);
}
