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

    // A18 / Abstractions 5.0 — preferred product addressing. Modules must not reinvent this.
    public static NeuronId ForPrincipal<TNeuron>(OwnerId owner, PrincipalId principal, string localName)
        where TNeuron : INeuron
        => new(
            GrainTypeNameOf(typeof(TNeuron)),
            owner,
            PrincipalPartition.InstanceName(principal, localName));

    public static NeuronId ForPrincipal(string grainType, OwnerId owner, PrincipalId principal, string localName)
        => new(grainType, owner, PrincipalPartition.InstanceName(principal, localName));

    public static NeuronId BroadcastReceiver(string type, OwnerId owner, CorrelationId correlation)
        => new(type, owner, correlation.Value.ToString("D"));

    public static string GrainTypeNameOf(Type neuronType)
    {
        ArgumentNullException.ThrowIfNull(neuronType);

        var declared = neuronType.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType == typeof(GrainTypeAttribute))?
            .ConstructorArguments[0].Value as string;

        if (declared is not null)
        {
            return declared;
        }

        const string OrleansGrainSuffix = "Grain";
        var name = neuronType.Name;

        if (neuronType.IsInterface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            return name[1..];
        }

        return name.Length > OrleansGrainSuffix.Length && name.EndsWith(OrleansGrainSuffix, StringComparison.Ordinal)
            ? name[..^OrleansGrainSuffix.Length]
            : name;
    }

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
