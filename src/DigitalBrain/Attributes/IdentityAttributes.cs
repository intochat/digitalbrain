namespace DigitalBrain;

/// <summary>Marks a signal record. The alias is frozen: it is written into persisted payloads.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SignalAttribute(string alias) : Attribute
{
    /// <summary>The frozen signal alias.</summary>
    public string Alias { get; } = alias;
}

/// <summary>Marks a signal as ephemeral (fast-path in-memory broadcast without durable journal append).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EphemeralSignalAttribute : Attribute;

/// <summary>Marks a versioned capability contract, written <c>name@version</c>.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class CapabilityAttribute(string name, int version) : Attribute
{
    /// <summary>The capability name.</summary>
    public string Name { get; } = name;

    /// <summary>The contract version.</summary>
    public int Version { get; } = version;

    /// <summary>The typed id, <c>name@version</c>.</summary>
    public DigitalBrain.Capabilities.CapabilityId Id => new(Name, Version);
}
