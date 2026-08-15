using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.deny-user-action")]
public sealed record DenyUserAction(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ProtectedPayloadReference ActionReference,
    [property: Id(2)] Guid ActionEpoch,
    [property: Id(3)] long ExpectedParkRevision) : Synapse;

