using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.resolve-operation")]
public sealed record ResolveOperation(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] OperationResolution Resolution,
    [property: Id(2)] ProtectedPayloadReference? ResponsePayload = null,
    [property: Id(3)] string? RedactedSummary = null) : ExecutionApplyCommand;

