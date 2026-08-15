using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-failed")]
public sealed record AttemptFailed(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] Failure Failure,
    [property: Id(1)] bool Retryable) : AttemptFact(Execution, Worker, Attempt, Revision);

