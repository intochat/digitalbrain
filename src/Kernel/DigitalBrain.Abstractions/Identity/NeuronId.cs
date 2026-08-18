using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.neuron-id")]
public readonly record struct NeuronId
{
    [JsonConstructor]
    public NeuronId(string type, OwnerId owner, string name)
    {
        Type = IdentityPart.Validated(type, nameof(type)).ToLowerInvariant();
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

    public GrainId ToGrainId() => GrainId.Create(Type, GrainKey);

    public static NeuronId For<TNeuron>(OwnerId owner, string name)
        where TNeuron : INeuron
        => new(GrainTypeNameOf(typeof(TNeuron)), owner, name);

    public static NeuronId BroadcastReceiver(string type, OwnerId owner, CorrelationId correlation)
        => new(type, owner, correlation.Value.ToString("D"));

    public static string GrainTypeNameOf(Type neuronType) => GrainTypeNames.Of(neuronType);

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
