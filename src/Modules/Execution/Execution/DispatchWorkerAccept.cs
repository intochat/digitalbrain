using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.dispatch-worker-accept")]
public sealed record DispatchWorkerAccept(
    [property: Id(0)] AttemptRequest Request) : Synapse;

