namespace DigitalBrain.Runtime;

[GenerateSerializer]
public readonly record struct CorrelationId([property: Id(0)] string Value)
{
    public static CorrelationId New() => new(Guid.NewGuid().ToString("N"));
    public bool IsEmpty => string.IsNullOrEmpty(Value);
    public override string ToString() => Value;
}
