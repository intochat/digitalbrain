namespace Brain.Contracts;

public enum ExternalOperationStatus
{
    Pending,
    Succeeded,
    Failed,
    Unknown
}

[GenerateSerializer]
[Alias(nameof(ExternalOperation))]
public sealed record ExternalOperation(
    [property: Id(0)] Guid OperationId,
    [property: Id(1)] ExternalOperationStatus Status,
    [property: Id(2)] NeuronFailureKind? FailureKind);
