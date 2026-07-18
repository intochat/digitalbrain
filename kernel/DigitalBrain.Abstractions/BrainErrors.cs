namespace DigitalBrain;

public enum NeuronFailureKind
{
    AuthenticationRequired,
    AuthorizationDenied,
    ProviderUnavailable,
    OperationFailed,
    OperationUnknown,
    StorageUnavailable
}

public enum NeuronStatus
{
    Idle,
    Active,
    Degraded
}

[GenerateSerializer]
[Alias(nameof(BrainException))]
public sealed class BrainException : Exception
{
    public BrainException(NeuronFailureKind failureKind, string detail)
        : base($"{failureKind}: {detail}")
    {
        FailureKind = failureKind;
    }

    [Id(0)]
    public NeuronFailureKind FailureKind { get; }
}
