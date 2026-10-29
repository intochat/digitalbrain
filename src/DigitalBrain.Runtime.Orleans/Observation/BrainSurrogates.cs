using Orleans;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Orleans wire form of <see cref="BrainInstance"/>; the same field ids and alias as the directory row it replaces.</summary>
[GenerateSerializer, Alias("kernel.instance")]
internal struct BrainInstanceSurrogate
{
    [Id(0)] public string Type;
    [Id(1)] public string Key;
    [Id(2)] public DateTimeOffset Created;
    [Id(3)] public string? Creator;
}

[RegisterConverter]
internal sealed class BrainInstanceSurrogateConverter : IConverter<BrainInstance, BrainInstanceSurrogate>
{
    public BrainInstance ConvertFromSurrogate(in BrainInstanceSurrogate surrogate)
        => new(surrogate.Type, surrogate.Key, surrogate.Created, surrogate.Creator);

    public BrainInstanceSurrogate ConvertToSurrogate(in BrainInstance value)
        => new() { Type = value.Type, Key = value.Key, Created = value.Created, Creator = value.Creator };
}

/// <summary>Orleans wire form of <see cref="TypeKind"/>, alias <c>kernel.type-kind</c>.</summary>
[GenerateSerializer, Alias("kernel.type-kind")]
internal enum TypeKindSurrogate { Neuron, Signal, Connector }

[RegisterConverter]
internal sealed class TypeKindSurrogateConverter : IConverter<TypeKind, TypeKindSurrogate>
{
    public TypeKind ConvertFromSurrogate(in TypeKindSurrogate surrogate) => (TypeKind)(int)surrogate;

    public TypeKindSurrogate ConvertToSurrogate(in TypeKind value) => (TypeKindSurrogate)(int)value;
}

/// <summary>Orleans wire form of <see cref="BrainType"/>; the same field ids and alias as the type-table row it replaces.</summary>
[GenerateSerializer, Alias("kernel.type-entry")]
internal struct BrainTypeSurrogate
{
    [Id(0)] public string Name;
    [Id(1)] public string Alias;
    [Id(2)] public TypeKindSurrogate Kind;
    [Id(3)] public string Origin;
    [Id(4)] public string Description;
    [Id(5)] public string[] Shape;
    [Id(6)] public string[] Implements;
}

[RegisterConverter]
internal sealed class BrainTypeSurrogateConverter : IConverter<BrainType, BrainTypeSurrogate>
{
    public BrainType ConvertFromSurrogate(in BrainTypeSurrogate surrogate)
        => new(surrogate.Name, surrogate.Alias, (TypeKind)(int)surrogate.Kind, surrogate.Origin, surrogate.Description, surrogate.Shape, surrogate.Implements);

    public BrainTypeSurrogate ConvertToSurrogate(in BrainType value)
        => new()
        {
            Name = value.Name,
            Alias = value.Alias,
            Kind = (TypeKindSurrogate)(int)value.Kind,
            Origin = value.Origin,
            Description = value.Description,
            Shape = value.Shape,
            Implements = value.Implements,
        };
}
