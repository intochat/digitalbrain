namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.pending-worker-accept")]
internal sealed record AcceptWorkerDispatch(
    [property: Id(0)] AttemptRequest Request) : PendingWorkerDispatch;

