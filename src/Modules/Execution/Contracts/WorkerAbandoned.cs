namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.worker-abandoned")]
public sealed record WorkerAbandoned(
    [property: Id(0)] string Reason) : Failure;
