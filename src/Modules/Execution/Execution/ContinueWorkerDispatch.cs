namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.pending-worker-continue")]
internal sealed record ContinueWorkerDispatch(
    [property: Id(0)] AttemptCursor Cursor) : PendingWorkerDispatch;

