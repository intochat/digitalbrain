namespace DigitalBrain.Abstractions.Descriptors;

/// <summary>Exposes a grain method through the JSON tool adapter. Unmarked methods remain Orleans-only.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class NeuronToolAttribute : Attribute
{
    /// <summary>Describes tool side effects; does not change Orleans request scheduling.</summary>
    public bool IsReadOnly { get; init; }
}
