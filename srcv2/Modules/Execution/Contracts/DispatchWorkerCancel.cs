using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.dispatch-worker-cancel")]
public sealed record DispatchWorkerCancel(
    [property: Id(0)] AttemptCursor Cursor) : Synapse;