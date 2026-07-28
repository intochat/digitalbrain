namespace DigitalBrain.Abstractions;

using System.Text.Json.Serialization;

[GenerateSerializer]
[Alias("db.behavior-revision-id")]
public readonly record struct BehaviorRevisionId
{
    [JsonConstructor]
    public BehaviorRevisionId(string value) => Value = Validate(value);

    [Id(0)]
    public string Value { get; }

    public static BehaviorRevisionId Parse(string value) => new(value);

    public void EnsureValid()
    {
        if (Value is null)
        {
            throw new InvalidOperationException("A behavior revision id has not been initialized.");
        }

        Validate(Value);
    }

    public bool Equals(BehaviorRevisionId other)
    {
        EnsureValid();
        other.EnsureValid();
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        EnsureValid();
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        EnsureValid();
        return Value;
    }

    private static string Validate(string value)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new FormatException("A behavior revision id must be a 64-character lowercase hexadecimal SHA-256 digest.");
        }

        return value;
    }
}
