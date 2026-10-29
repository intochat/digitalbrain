namespace DigitalBrain.Contracts;

/// <summary>Marks a record or class whose persisted and wire layout a runtime must generate.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class NeuronContractAttribute : Attribute;

/// <summary>The rename-safe type name: the signal id and the type name written into persisted polymorphic payloads.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, Inherited = false)]
public sealed class NeuronAliasAttribute(string alias) : Attribute
{
    /// <summary>The frozen alias.</summary>
    public string Alias { get; } = alias;
}

/// <summary>The stable member id inside its type's layout.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class FieldAttribute(uint id) : Attribute
{
    /// <summary>The stable member id.</summary>
    public uint Id { get; } = id;
}

/// <summary>The neuron type an interface resolves to by default.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class DefaultNeuronTypeAttribute(string type) : Attribute
{
    /// <summary>The default neuron type name.</summary>
    public string Type { get; } = type;
}
