namespace DigitalBrain.Abstractions;

using System.Text.Json.Serialization;

[GenerateSerializer]
[Alias("db.behavior-execution-id")]
public readonly record struct BehaviorExecutionId
{
    [JsonConstructor]
    public BehaviorExecutionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A behavior execution id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static BehaviorExecutionId New() => new(Guid.NewGuid());

    public void EnsureValid()
    {
        if (Value == Guid.Empty)
        {
            throw new InvalidOperationException("A behavior execution id has not been initialized.");
        }
    }

    public bool Equals(BehaviorExecutionId other)
    {
        EnsureValid();
        other.EnsureValid();
        return Value == other.Value;
    }

    public override int GetHashCode()
    {
        EnsureValid();
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        EnsureValid();
        return Value.ToString("n");
    }
}
