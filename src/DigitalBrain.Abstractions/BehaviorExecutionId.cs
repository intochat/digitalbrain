namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior-execution-id")]
public readonly record struct BehaviorExecutionId
{
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

    public override string ToString() => Value.ToString("n");
}
