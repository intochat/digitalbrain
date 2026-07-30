using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.snapshot")]
public sealed record TaskSnapshot(
    [property: Id(0)] Goal Goal,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] TaskPolicy Policy,
    [property: Id(3)] TaskState State,
    [property: Id(4)] long Revision,
    [property: Id(5)] AttemptId? ActiveAttempt,
    [property: Id(6)] TaskBlocker? Blocker,
    [property: Id(7)] Result? Result,
    [property: Id(8)] Failure? Failure,
    [property: Id(9)] IReadOnlyList<FactReference> Evidence,
    [property: Id(10)] NeuronId? RetryOf,
    [property: Id(11)] int AttemptCount,
    [property: Id(12)] BehaviorTaskActivation? Activation) : Synapse;
