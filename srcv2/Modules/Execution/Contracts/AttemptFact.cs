using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-fact")]
public abstract record AttemptFact(
    [property: Id(0)] NeuronId Execution,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] AttemptId Attempt,
    [property: Id(3)] long Revision) : Synapse;

