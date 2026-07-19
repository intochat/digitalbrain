namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.synapse-id")]
public readonly record struct SynapseId([property: Id(0)] Guid Value)
{
    public static SynapseId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
