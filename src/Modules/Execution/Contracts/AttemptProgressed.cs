using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-progressed")]
public sealed record AttemptProgressed(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision) : AttemptFact(Execution, Worker, Attempt, Revision);

