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

public abstract record ExternalOperationTransition
{
    public sealed record Succeeded : ExternalOperationTransition;

    public sealed record Failed(NeuronFailureKind FailureKind) : ExternalOperationTransition;

    public sealed record Unknown(NeuronFailureKind FailureKind) : ExternalOperationTransition;

    public sealed record ReconcileSucceeded : ExternalOperationTransition;
}

public static class ExternalOperationTransitions
{
    public static ExternalOperation Apply(
        ExternalOperation current,
        ExternalOperationTransition transition) =>
        (current.Status, transition) switch
        {
            (ExternalOperationStatus.Pending, ExternalOperationTransition.Succeeded) =>
                current with { Status = ExternalOperationStatus.Succeeded, FailureKind = null },
            (ExternalOperationStatus.Pending, ExternalOperationTransition.Failed failed) =>
                current with { Status = ExternalOperationStatus.Failed, FailureKind = failed.FailureKind },
            (ExternalOperationStatus.Pending, ExternalOperationTransition.Unknown unknown) =>
                current with { Status = ExternalOperationStatus.Unknown, FailureKind = unknown.FailureKind },
            (ExternalOperationStatus.Unknown, ExternalOperationTransition.ReconcileSucceeded) =>
                current with { Status = ExternalOperationStatus.Succeeded, FailureKind = null },
            (ExternalOperationStatus.Succeeded, ExternalOperationTransition.ReconcileSucceeded) =>
                current,
            (ExternalOperationStatus.Succeeded, ExternalOperationTransition.Succeeded) =>
                current,
            _ => throw new InvalidOperationException(
                $"Invalid transition from {current.Status} via {transition.GetType().Name}.")
        };
}
