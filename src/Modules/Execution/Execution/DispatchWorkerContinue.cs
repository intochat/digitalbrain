using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.dispatch-worker-continue")]
public sealed record DispatchWorkerContinue(
    [property: Id(0)] AttemptCursor Cursor) : Synapse;

