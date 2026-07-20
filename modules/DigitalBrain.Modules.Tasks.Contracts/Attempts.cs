using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.goal")]
public abstract record Goal;

[GenerateSerializer]
[Alias("tasks.result")]
public abstract record Result;

[GenerateSerializer]
[Alias("tasks.failure")]
public abstract record Failure;

[GenerateSerializer]
[Alias("tasks.attempt-id")]
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

[GenerateSerializer]
[Alias("tasks.blocker-id")]
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

[GenerateSerializer]
[Alias("tasks.fact-reference")]
public readonly record struct FactReference(
    [property: Id(0)] NeuronId Source,
    [property: Id(1)] SynapseId Fact);

[GenerateSerializer]
[Alias("tasks.attempt-request")]
public sealed record AttemptRequest(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] AttemptId Attempt,
    [property: Id(3)] long Revision,
    [property: Id(4)] Goal Goal);

[GenerateSerializer]
[Alias("tasks.attempt-cursor")]
public sealed record AttemptCursor(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] AttemptId Attempt,
    [property: Id(3)] long Revision);
