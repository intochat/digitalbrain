namespace Brain.Product.Abstractions.Authority;

public readonly record struct ConnectionReference
{
    public ConnectionReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}
