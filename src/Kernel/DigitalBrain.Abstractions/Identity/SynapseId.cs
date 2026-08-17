namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.synapse-id")]
public readonly record struct SynapseId
{
    public SynapseId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A synapse id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static SynapseId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
