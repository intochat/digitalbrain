namespace DigitalBrain.Marketplace;

public enum EarningsEntryKind
{
    Sale = 0,
    Refund = 1,
    Payout = 2,
    Clawback = 3,
    Adjustment = 4,
}

// A customer charge for an app. The amount the customer paid is the full price; tax is collected
// by IntoChat as merchant of record on that full price.
[GenerateSerializer, Alias("marketplace.app-charge")]
public sealed record AppCharge
{
    [Id(0)] public required string ChargeId { get; init; }
    [Id(1)] public required string AppId { get; init; }
    [Id(2)] public required decimal GrossUsd { get; init; }
    [Id(3)] public required decimal TaxUsd { get; init; }
    [Id(4)] public DateTimeOffset OccurredAt { get; init; }
}

// A refund is recorded as a new entry that reverses the original sale, never as an edit of it.
[GenerateSerializer, Alias("marketplace.refund")]
public sealed record RefundRequest
{
    [Id(0)] public required string ChargeId { get; init; }
    [Id(1)] public DateTimeOffset OccurredAt { get; init; }
    [Id(2)] public string? Reason { get; init; }
}

// Every line is fiat USD. Compute never appears here: platform meters are a separate ledger.
[GenerateSerializer, Alias("marketplace.earnings-entry")]
public sealed record EarningsEntry
{
    [Id(0)] public required string EntryId { get; init; }
    [Id(1)] public required EarningsEntryKind Kind { get; init; }
    [Id(2)] public required string CreatorId { get; init; }
    [Id(3)] public string? AppId { get; init; }
    [Id(4)] public string? ChargeId { get; init; }
    [Id(5)] public required decimal GrossUsd { get; init; }
    [Id(6)] public required decimal TaxUsd { get; init; }
    [Id(7)] public required decimal FeeUsd { get; init; }
    [Id(8)] public required decimal NetUsd { get; init; }
    [Id(9)] public required DateTimeOffset OccurredAt { get; init; }
    [Id(10)] public required DateTimeOffset AvailableAt { get; init; }
    [Id(11)] public string? PayoutId { get; init; }
    [Id(12)] public string? Description { get; init; }
}

[GenerateSerializer, Alias("marketplace.earnings-balance")]
public sealed record EarningsBalance
{
    [Id(0)] public required decimal AvailableUsd { get; init; }
    [Id(1)] public required decimal HeldUsd { get; init; }
    [Id(2)] public required decimal PaidOutUsd { get; init; }
    [Id(3)] public required decimal LifetimeNetUsd { get; init; }
}

public enum PayoutOutcome
{
    Paid = 0,
    Blocked = 1,
    BelowThreshold = 2,
    Failed = 3,
}

[GenerateSerializer, Alias("marketplace.payout")]
public sealed record Payout
{
    [Id(0)] public required string PayoutId { get; init; }
    [Id(1)] public required string CreatorId { get; init; }
    [Id(2)] public required decimal AmountUsd { get; init; }
    [Id(3)] public required bool Paid { get; init; }
    [Id(4)] public string? ProviderReference { get; init; }
    [Id(5)] public string? FailureReason { get; init; }
    [Id(6)] public required DateTimeOffset RequestedAt { get; init; }
}

[GenerateSerializer, Alias("marketplace.payout-outcome")]
public sealed record PayoutResult
{
    [Id(0)] public required PayoutOutcome Outcome { get; init; }
    [Id(1)] public Payout? Payout { get; init; }
    [Id(2)] public string? Reason { get; init; }
}
