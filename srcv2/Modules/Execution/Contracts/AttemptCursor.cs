using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.attempt-cursor")]
public sealed record AttemptCursor(
    [property: Id(0)] NeuronId Execution,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] AttemptId Attempt,
    [property: Id(3)] long Revision);
