namespace DigitalBrain.Capabilities.Compute;

public sealed record ExternalCallRequest
{
    public required string Operation { get; init; }
    public required decimal Quantity { get; init; }
    public required string Unit { get; init; }
    public required Funding Funding { get; init; }
    public required string InvocationId { get; init; }
    public required string CostId { get; init; }
    public CostSubject? Subject { get; init; }
}

// An opaque capability validated by Authority against its durable reservation.
public readonly record struct ExternalCallHandle(string Value);

public enum ExternalCallOutcomeKind
{
    Succeeded = 0,
    HttpFailed = 1,
    TransportFailed = 2,
    CancelledBeforeDispatch = 3,
    CancelledAfterDispatch = 4,
    Uncertain = 5,
}

public sealed record ExternalCallOutcome(
    ExternalCallOutcomeKind Kind,
    int? HttpStatusCode = null,
    string? ProviderRequestId = null);

public enum ExternalAccountingStatus
{
    Pending = 0,
    Settled = 1,
    CancelledBeforeDispatch = 2,
}

public enum ExternalCallState
{
    Reserved = 0,
    DispatchStarted = 1,
    OutcomeKnown = 2,
    Settled = 3,
    CancelledBeforeDispatch = 4,
    Uncertain = 5,
}

public sealed record ExternalCallCompletion(
    ExternalCallHandle Handle,
    ExternalAccountingStatus Status,
    Cost? Cost = null);
