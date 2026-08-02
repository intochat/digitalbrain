namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.vector-namespace")]
public readonly struct VectorMemoryNamespace : IEquatable<VectorMemoryNamespace>
{
    public static VectorMemoryNamespace Capabilities { get; } = new("digitalbrain.capabilities");

    public static VectorMemoryNamespace Behaviors { get; } = new("digitalbrain.behaviors");

    public VectorMemoryNamespace(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    [Id(0)]
    public string Value { get; }

    public bool Equals(VectorMemoryNamespace other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is VectorMemoryNamespace other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(VectorMemoryNamespace left, VectorMemoryNamespace right) =>
        left.Equals(right);

    public static bool operator !=(VectorMemoryNamespace left, VectorMemoryNamespace right) =>
        !left.Equals(right);
}
