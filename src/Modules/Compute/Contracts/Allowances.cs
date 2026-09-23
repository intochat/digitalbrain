using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Compute;

public enum ApprovalLevel
{
    StandingBudget = 0,
    InstallConsent = 1,
    PerOperation = 2,
}

public enum AllowanceScope
{
    Once = 0,
    ThisChat = 1,
    Always = 2,
}

// One approved spending right. A standing budget covers a workspace, install consent covers an app
// up to a monthly limit, and a per-operation allowance covers one operation. Permissions and the
// price book version are recorded at grant time so a later permission or price growth forces
// re-consent instead of silently extending the approval.
[GenerateSerializer, Alias("compute.allowance")]
public sealed record Allowance
{
    [Id(0)] public required string AllowanceId { get; init; }
    [Id(1)] public required string AccountId { get; init; }
    [Id(2)] public required string WorkspaceId { get; init; }
    [Id(3)] public string? AppId { get; init; }
    [Id(4)] public string? Operation { get; init; }
    [Id(5)] public required ApprovalLevel Level { get; init; }
    [Id(6)] public required AllowanceScope Scope { get; init; }
    [Id(7)] public required decimal LimitCompute { get; init; }
    [Id(8)] public decimal MonthlyLimitCompute { get; init; }
    [Id(9)] public IReadOnlyList<string> Permissions { get; init; } = [];
    [Id(10)] public required string PriceBookVersion { get; init; }
    [Id(11)] public DateTimeOffset GrantedAt { get; init; }
    [Id(12)] public string? ConversationId { get; init; }
    [Id(13)] public bool Revoked { get; init; }
}

// A reservation is the authorization record for one paid call. Its id is derived from the intent
// and operation, so a retried or duplicated call reserves once.
[GenerateSerializer, Alias("compute.allowance-reservation")]
public sealed record AllowanceReservation
{
    [Id(0)] public required string ReservationId { get; init; }
    [Id(1)] public required string AllowanceId { get; init; }
    [Id(2)] public required decimal ReservedCompute { get; init; }
    [Id(3)] public decimal SettledCompute { get; init; }
    [Id(4)] public required DateTimeOffset OccurredAt { get; init; }
    [Id(5)] public string? IntentId { get; init; }
    [Id(6)] public required string WorkspaceId { get; init; }
    [Id(7)] public string? AppId { get; init; }
    [Id(8)] public required string Operation { get; init; }
    [Id(9)] public FailureClass Failure { get; init; }
    [Id(10)] public bool Settled { get; init; }
}

// An approval waiting on a human. It lives in the durable grain state, so it survives activation.
[GenerateSerializer, Alias("compute.pending-approval")]
public sealed record PendingApproval
{
    [Id(0)] public required string ApprovalId { get; init; }
    [Id(1)] public required string WorkspaceId { get; init; }
    [Id(2)] public string? AppId { get; init; }
    [Id(3)] public required string Operation { get; init; }
    [Id(4)] public required decimal EstimatedCompute { get; init; }
    [Id(5)] public required string Reason { get; init; }
    [Id(6)] public required DateTimeOffset RequestedAt { get; init; }
    [Id(7)] public string? IntentId { get; init; }
    [Id(8)] public IReadOnlyList<string> Permissions { get; init; } = [];
}

public enum LimitAlert
{
    None = 0,
    At75 = 1,
    At90 = 2,
    At100 = 3,
}

// Caps at account, workspace, app and run scope. Zero means "no cap at this scope".
[GenerateSerializer, Alias("compute.limit-policy")]
public sealed record LimitPolicy
{
    [Id(0)] public decimal AccountLimitCompute { get; init; }
    [Id(1)] public decimal WorkspaceLimitCompute { get; init; }
    [Id(2)] public decimal AppLimitCompute { get; init; }
    [Id(3)] public decimal RunLimitCompute { get; init; }
}

[GenerateSerializer, Alias("compute.limit-report")]
public sealed record LimitReport
{
    [Id(0)] public required string AccountId { get; init; }
    [Id(1)] public required decimal SpentCompute { get; init; }
    [Id(2)] public required decimal LimitCompute { get; init; }
    [Id(3)] public required LimitAlert HighestAlert { get; init; }
    [Id(4)] public DateTimeOffset? HardStoppedAt { get; init; }
    [Id(5)] public DateTimeOffset? ReadExportGraceUntil { get; init; }
    public bool HardStopped => HardStoppedAt is not null;
}

[GenerateSerializer, Alias("compute.allowance-decision")]
public sealed record AllowanceDecision
{
    [Id(0)] public bool Allowed { get; init; }
    [Id(1)] public CallDenial? Denial { get; init; }
    [Id(2)] public string? Explanation { get; init; }
    [Id(3)] public string? ReservationId { get; init; }
    [Id(4)] public string? ApprovalId { get; init; }
    [Id(5)] public decimal RemainingCompute { get; init; }
    [Id(6)] public string? AllowanceId { get; init; }

    public static AllowanceDecision Allow() => new() { Allowed = true };
}

// The task's failure taxonomy. Only platform/provider/third-party faults are never charged; a model
// retry inside a successful intent, a cancellation, a revoked grant and a limit are charged for the
// steps that completed, capped at the remaining limit.
public enum FailureClass
{
    None = 0,
    PlatformFault = 1,
    ProviderOutage = 2,
    ModelRetry = 3,
    Cancelled = 4,
    RevokedGrant = 5,
    LimitReached = 6,
    ThirdPartyFault = 7,
}

[GenerateSerializer, Alias("compute.alert")]
public sealed record ComputeLimitAlert
{
    [Id(0)] public required string WorkspaceId { get; init; }
    [Id(1)] public required LimitAlert Level { get; init; }
    [Id(2)] public required decimal SpentCompute { get; init; }
    [Id(3)] public required decimal LimitCompute { get; init; }
}

// Best-effort notification hook. The product implementation posts an InboxItemDraft to IInboxFeed;
// a failed notification must never fail the charging decision.
public interface IComputeAlertSink
{
    Task RaiseAsync(ComputeLimitAlert alert, CancellationToken cancellationToken = default);
}

// Per account. The one durable writer of allowances, reservations, pending approvals and limits.
[Alias("compute-allowance-ledger")]
[Orleans.Metadata.DefaultGrainType("compute-allowance-ledger")]
public interface IAllowanceLedger : INeuron
{
    Task<AllowanceDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken = default);

    Task<Allowance> GrantAsync(Allowance allowance, CancellationToken cancellationToken = default);

    Task<PendingApproval[]> ListPendingAsync(CancellationToken cancellationToken = default);

    Task<Allowance> ApproveAsync(string approvalId, ApprovalLevel level, AllowanceScope scope, decimal limitCompute, CancellationToken cancellationToken = default);

    Task DenyAsync(string approvalId, CancellationToken cancellationToken = default);

    Task<AllowanceReservation> SettleAsync(string reservationId, decimal actualCompute, FailureClass failure, CancellationToken cancellationToken = default);

    Task<LimitPolicy> SetLimitsAsync(LimitPolicy policy, CancellationToken cancellationToken = default);

    Task<LimitReport> ReadLimitsAsync(CancellationToken cancellationToken = default);

    Task<Allowance[]> ReadAllowancesAsync(CancellationToken cancellationToken = default);

    Task<AllowanceReservation[]> ReadReservationsAsync(CancellationToken cancellationToken = default);
}
