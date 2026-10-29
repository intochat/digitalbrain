namespace DigitalBrain;

// Scheduling semantics declared on a contract and honoured by every runtime. A neuron runs one turn at a time
// unless one of these says otherwise.

/// <summary>The call may run while another turn of the same neuron is in progress.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class InterleavesAttribute : Attribute;

/// <summary>Names a static predicate on the class that decides, per call, whether the call may interleave.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class InterleavesWhenAttribute(string predicate) : Attribute
{
    /// <summary>The name of the static predicate.</summary>
    public string Predicate { get; } = predicate;
}

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

/// <summary>Opts a neuron class into reentrancy: any call may interleave with any other.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ReentrantAttribute : Attribute;

/// <summary>Marks a neuron class that keeps no state: the host may run many activations of it and route calls to any.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StatelessNeuronAttribute : Attribute;
