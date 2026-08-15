using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-succeeded")]
public sealed record AttemptSucceeded(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] Result Result,
    [property: Id(1)] IReadOnlyList<FactReference> Evidence) : AttemptFact(Execution, Worker, Attempt, Revision);

