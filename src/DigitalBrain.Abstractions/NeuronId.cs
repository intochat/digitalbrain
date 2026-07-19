using Orleans;

namespace DigitalBrain;

[GenerateSerializer]
[Alias("db.neuron-id")]
public readonly record struct NeuronId
{
    public NeuronId(string type, OwnerId owner, string name)
    {
        Type = IdentityPart.Validated(type, nameof(type));
        Owner = owner;
        Name = IdentityPart.Validated(name, nameof(name));
    }

    [Id(0)]
    public string Type { get; }

    [Id(1)]
    public OwnerId Owner { get; }

    [Id(2)]
    public string Name { get; }

    public string GrainKey => $"{Owner.Value}{IdentityPart.OwnerNameSeparator}{Name}";

    public static NeuronId FromGrainKey(string type, string grainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainKey);

        var separator = grainKey.IndexOf(IdentityPart.OwnerNameSeparator, StringComparison.Ordinal);

        if (separator <= 0 || separator == grainKey.Length - 1)
        {
            throw new ArgumentException($"Grain key '{grainKey}' is not in owner/name form.", nameof(grainKey));
        }

        return new NeuronId(type, new OwnerId(grainKey[..separator]), grainKey[(separator + 1)..]);
    }

    public override string ToString() => $"{Type}:{GrainKey}";
}
