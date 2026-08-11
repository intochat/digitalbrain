namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.pending-worker-dispatch")]
internal abstract record PendingWorkerDispatch;

[GenerateSerializer]
[Alias("db.execution.pending-worker-accept")]
internal sealed record AcceptWorkerDispatch(
    [property: Id(0)] AttemptRequest Request) : PendingWorkerDispatch;

[GenerateSerializer]
[Alias("db.execution.pending-worker-continue")]
internal sealed record ContinueWorkerDispatch(
    [property: Id(0)] AttemptCursor Cursor) : PendingWorkerDispatch;

[GenerateSerializer]
[Alias("db.execution.pending-worker-cancel")]
internal sealed record CancelWorkerDispatch(
    [property: Id(0)] AttemptCursor Cursor) : PendingWorkerDispatch;
