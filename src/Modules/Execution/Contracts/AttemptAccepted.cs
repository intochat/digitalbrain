using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-accepted")]
public sealed record AttemptAccepted(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Execution, Worker, Attempt, Revision);

