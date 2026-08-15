namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.blocker-id")]
public readonly record struct BlockerId
{
    public BlockerId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A blocker id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }
}
