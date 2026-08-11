using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-fact")]
public abstract record AttemptFact(
    [property: Id(0)] NeuronId Execution,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] AttemptId Attempt,
    [property: Id(3)] long Revision) : Synapse;

[GenerateSerializer]
[Alias("db.execution.attempt-accepted")]
public sealed record AttemptAccepted(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Execution, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("db.execution.attempt-progressed")]
public sealed record AttemptProgressed(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Execution, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("db.execution.attempt-waiting")]
public sealed record AttemptWaiting(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] ExecutionBlocker Blocker) : AttemptFact(Execution, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("db.execution.attempt-succeeded")]
public sealed record AttemptSucceeded(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] Result Result,
    [property: Id(1)] IReadOnlyList<FactReference> Evidence) : AttemptFact(Execution, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("db.execution.attempt-failed")]
public sealed record AttemptFailed(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] Failure Failure,
    [property: Id(1)] bool Retryable) : AttemptFact(Execution, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("db.execution.attempt-cancelled")]
public sealed record AttemptCancelled(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Execution, Worker, Attempt, Revision);

[GenerateSerializer]
[Alias("db.execution.attempt-outcome-uncertain")]
public sealed record AttemptOutcomeUncertain(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] BlockerId Blocker) : AttemptFact(Execution, Worker, Attempt, Revision);
