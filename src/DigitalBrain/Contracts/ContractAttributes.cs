namespace DigitalBrain.Contracts;

/// <summary>The rename-safe type name: the signal id and the type name written into persisted polymorphic payloads.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, Inherited = false)]
public sealed class NeuronAliasAttribute(string alias) : Attribute
{
    /// <summary>The frozen alias.</summary>
    public string Alias { get; } = alias;
}

/// <summary>The neuron type an interface resolves to by default.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class DefaultNeuronTypeAttribute(string type) : Attribute
{
    /// <summary>The default neuron type name.</summary>
    public string Type { get; } = type;
}

/// <summary>The call may run while another turn of the same neuron is in progress.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class InterleavesAttribute : Attribute;

/// <summary>The call does not change state, so read-only calls may interleave with each other.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ReadOnlyCallAttribute : Attribute;

/// <summary>The caller does not wait for completion.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class OneWayCallAttribute : Attribute;

/// <summary>The call's response timeout.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CallTimeoutAttribute : Attribute
{
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="milliseconds"/> is not positive.</exception>
    public CallTimeoutAttribute(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(milliseconds);
        Milliseconds = milliseconds;
    }

    /// <summary>The timeout in milliseconds.</summary>
    public int Milliseconds { get; }

    /// <summary>The timeout.</summary>
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(Milliseconds);
}

