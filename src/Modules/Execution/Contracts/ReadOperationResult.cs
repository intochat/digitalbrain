using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.read-operation-result")]
public sealed record ReadOperationResult(
    [property: Id(0)] OperationSnapshot? Operation) : Synapse;

