using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.read-operation")]
public sealed record ReadOperation(
    [property: Id(0)] string OperationKey) : RequestSynapse<ReadOperationResult>;

