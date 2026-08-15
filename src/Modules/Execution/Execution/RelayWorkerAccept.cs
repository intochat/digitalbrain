using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.relay-worker-accept")]
public sealed record RelayWorkerAccept(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptRequest Request) : Synapse;

