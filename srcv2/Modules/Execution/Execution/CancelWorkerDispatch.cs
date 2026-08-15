namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.pending-worker-cancel")]
internal sealed record CancelWorkerDispatch(
    [property: Id(0)] AttemptCursor Cursor) : PendingWorkerDispatch;

