using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-waiting")]
public sealed record AttemptWaiting(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] ExecutionBlocker Blocker) : AttemptFact(Execution, Worker, Attempt, Revision);

