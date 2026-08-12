using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.complete-user-action")]
public sealed record CompleteUserAction(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ProtectedPayloadReference ActionReference,
    [property: Id(2)] Guid ActionEpoch,
    [property: Id(3)] long ExpectedParkRevision) : Synapse;

