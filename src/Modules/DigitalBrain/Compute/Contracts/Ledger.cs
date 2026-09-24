using DigitalBrain.Contracts;

namespace DigitalBrain.Compute;

public enum LedgerKind
{
    WalletCharge = 0,
    PlatformCost = 1,
}

[GenerateSerializer, Alias("compute.ledger-entry")]
public sealed record LedgerEntry
{
    [Id(0)] public required string AccountId { get; init; }
    [Id(1)] public required string IdempotencyKey { get; init; }
    [Id(2)] public required LedgerKind Kind { get; init; }
    [Id(3)] public required decimal Amount { get; init; }
    [Id(4)] public string? IntentId { get; init; }
    [Id(5)] public string? AppId { get; init; }
    [Id(6)] public string? Description { get; init; }
    [Id(7)] public required DateTimeOffset OccurredAt { get; init; }
}

public enum LedgerAppend
{
    Inserted = 0,
    Duplicate = 1,
}

[GenerateSerializer, Alias("compute.wallet-balance")]
public sealed record WalletBalance(
    [property: Id(0)] string AccountId,
    [property: Id(1)] decimal ChargedCompute,
    [property: Id(2)] decimal CostCompute,
    [property: Id(3)] int EntryCount);

[Alias("compute.wallet")]
[Orleans.Metadata.DefaultGrainType("compute-wallet")]
public interface IWallet : INeuron
{
    Task<LedgerAppend> ChargeAsync(LedgerEntry charge, CancellationToken cancellationToken = default);

    Task<LedgerAppend> RecordCostAsync(LedgerEntry cost, CancellationToken cancellationToken = default);

    Task<WalletBalance> ReadBalanceAsync(CancellationToken cancellationToken = default);

    Task<LedgerEntry[]> ReadEntriesAsync(CancellationToken cancellationToken = default);
}

[Alias("compute.cost-ledger")]
[Orleans.Metadata.DefaultGrainType("compute-cost-ledger")]
public interface ICostLedger : INeuron
{
    Task<LedgerAppend> RecordAsync(LedgerEntry cost, CancellationToken cancellationToken = default);

    Task<LedgerEntry[]> ReadAsync(CancellationToken cancellationToken = default);
}
