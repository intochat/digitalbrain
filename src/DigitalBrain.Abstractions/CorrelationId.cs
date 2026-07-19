namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.correlation-id")]
public readonly record struct CorrelationId([property: Id(0)] Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
