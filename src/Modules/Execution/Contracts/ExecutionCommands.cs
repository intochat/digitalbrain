using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.policy")]
public sealed record ExecutionPolicy(
    [property: Id(0)] int MaximumAttempts,
    [property: Id(1)] TimeSpan RetryDelay,
    [property: Id(2)] DateTimeOffset? Deadline);

[GenerateSerializer]
[Alias("db.execution.apply-command")]
public abstract record ExecutionApplyCommand;

[GenerateSerializer]
[Alias("db.execution.start")]
public sealed record StartExecution(
    [property: Id(0)] Goal Goal,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] ExecutionPolicy Policy,
    [property: Id(3)] NeuronId? RetryOf = null,
    [property: Id(4)] NeuronId? Origin = null) : ExecutionApplyCommand;

[GenerateSerializer]
[Alias("db.execution.cancel")]
public sealed record CancelExecution : ExecutionApplyCommand;

[GenerateSerializer]
[Alias("db.execution.operation-resolution")]
public enum OperationResolution
{
    Completed = 0,
    Failed = 1,
    PermitRetry = 2,
}

[GenerateSerializer]
[Alias("db.execution.resolve-operation")]
public sealed record ResolveOperation(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] OperationResolution Resolution,
    [property: Id(2)] ProtectedPayloadReference? ResponsePayload = null,
    [property: Id(3)] string? RedactedSummary = null) : ExecutionApplyCommand;

[GenerateSerializer]
[Alias("db.execution.apply")]
public sealed record ApplyExecution(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ExecutionApplyCommand Command,
    [property: Id(2)] long? ExpectedRevision = null) : RequestSynapse<ExecutionSnapshot>;
