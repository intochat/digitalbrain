namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-id")]
public readonly record struct AttemptId
{
    public AttemptId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An attempt id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }
}
