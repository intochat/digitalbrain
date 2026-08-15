using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.snapshot")]
public sealed record ExecutionSnapshot(
    [property: Id(0)] Goal Goal,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] ExecutionPolicy Policy,
    [property: Id(3)] ExecutionState State,
    [property: Id(4)] long Revision,
    [property: Id(5)] AttemptId? ActiveAttempt,
    [property: Id(6)] ExecutionBlocker? Blocker,
    [property: Id(7)] Result? Result,
    [property: Id(8)] Failure? Failure,
    [property: Id(9)] IReadOnlyList<FactReference> Evidence,
    [property: Id(10)] NeuronId? RetryOf,
    [property: Id(11)] int AttemptCount) : Synapse;
