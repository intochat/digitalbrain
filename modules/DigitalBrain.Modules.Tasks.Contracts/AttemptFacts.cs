using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.attempt-fact")]
public abstract record AttemptFact(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] AttemptId Attempt,
    [property: Id(3)] long Revision) : Synapse;

[GenerateSerializer]
[Alias("tasks.attempt-accepted")]
public sealed record AttemptAccepted(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Task, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("tasks.attempt-advanced")]
public sealed record AttemptAdvanced(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Task, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("tasks.attempt-progressed")]
public sealed record AttemptProgressed(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Task, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("tasks.attempt-waiting")]
public sealed record AttemptWaiting(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] TaskBlocker Blocker) : AttemptFact(Task, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("tasks.attempt-succeeded")]
public sealed record AttemptSucceeded(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] Result Result,
    [property: Id(1)] IReadOnlyList<FactReference> Evidence) : AttemptFact(Task, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("tasks.attempt-failed")]
public sealed record AttemptFailed(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] Failure Failure,
    [property: Id(1)] bool Retryable) : AttemptFact(Task, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("tasks.attempt-cancelled")]
public sealed record AttemptCancelled(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Task, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("tasks.attempt-outcome-uncertain")]
public sealed record AttemptOutcomeUncertain(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] BlockerId Blocker) : AttemptFact(Task, Worker, Attempt, Revision);
