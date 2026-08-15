using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.operation-edge")]
public sealed record OperationEdge(
    [property: Id(0)] NeuronId Target,
    [property: Id(1)] string RequestSynapseId,
    [property: Id(2)] int RequestSchemaVersion,
    [property: Id(3)] string ResponseSynapseId,
    [property: Id(4)] int ResponseSchemaVersion);

