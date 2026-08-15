using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-outcome-uncertain")]
public sealed record AttemptOutcomeUncertain(
    NeuronId Execution,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    [property: Id(0)] BlockerId Blocker) : AttemptFact(Execution, Worker, Attempt, Revision);

