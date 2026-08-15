using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-cancelled")]
public sealed record AttemptCancelled(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Execution, Worker, Attempt, Revision);

