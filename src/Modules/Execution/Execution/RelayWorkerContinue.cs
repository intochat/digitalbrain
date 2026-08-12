using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.relay-worker-continue")]
public sealed record RelayWorkerContinue(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptCursor Cursor) : Synapse;

