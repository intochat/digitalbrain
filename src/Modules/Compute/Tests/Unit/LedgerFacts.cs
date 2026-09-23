using DigitalBrain.Compute;
using DigitalBrain.Compute.Ledger;
using DigitalBrain.Testing.Unit;
using Npgsql;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class LedgerFacts
{
    [Fact]
    public async Task ConcurrentWritersNeverDoubleCharge()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryLedgerStore();

        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => store.AppendAsync(Charge("charge-intent-1", 7m), ct).AsTask()));

        Assert.Single(results, result => result == LedgerAppend.Inserted);
        var entries = await store.ReadAsync("account", ct);
        Assert.Single(entries);
        Assert.Equal(7m, entries[0].Amount);
    }

    [Fact]
    public async Task WalletGrainChargesOnceForARepeatedKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var wallet = brain.Get<IWallet>("account-a");

        var results = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => wallet.ChargeAsync(Charge("charge-intent-1", 5m), ct)));

        Assert.Single(results, result => result == LedgerAppend.Inserted);
        var balance = await wallet.ReadBalanceAsync(ct);
        Assert.Equal(5m, balance.ChargedCompute);
        Assert.Equal(0m, balance.CostCompute);
        Assert.Equal(1, balance.EntryCount);
    }

    [Fact]
    public async Task WalletKeepsChargesAndPlatformCostsSeparate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var wallet = brain.Get<IWallet>("account-b");

        await wallet.ChargeAsync(Charge("charge-intent-2", 10m), ct);
        await wallet.RecordCostAsync(Cost("provider-cost-1", 2.5m), ct);

        var balance = await wallet.ReadBalanceAsync(ct);
        Assert.Equal(10m, balance.ChargedCompute);
        Assert.Equal(2.5m, balance.CostCompute);
        Assert.Equal(2, balance.EntryCount);
        Assert.Equal(2, (await wallet.ReadEntriesAsync(ct)).Length);
    }

    [Fact]
    public async Task CostLedgerIsItsOwnWriterSeparateFromTheWallet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var costs = brain.Get<ICostLedger>("account-c");
        var wallet = brain.Get<IWallet>("account-c");

        await costs.RecordAsync(Cost("provider-cost-1", 2.5m), ct);

        Assert.Single(await costs.ReadAsync(ct));
        var balance = await wallet.ReadBalanceAsync(ct);
        Assert.Equal(0m, balance.ChargedCompute);
        Assert.Equal(2.5m, balance.CostCompute);
    }

    [Fact]
    public async Task PostgresLedgerEnforcesIdempotencyWhenADatabaseIsAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("DIGITALBRAIN_COMPUTE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("Set DIGITALBRAIN_COMPUTE_TEST_POSTGRES to run the PostgreSQL ledger test.");
        }

        await using var source = NpgsqlDataSource.Create(connectionString);
        var store = new PostgresLedgerStore(source);
        var key = "postgres-" + Guid.NewGuid().ToString("N");

        var first = await store.AppendAsync(Charge(key, 3m) with { AccountId = key }, ct);
        var second = await store.AppendAsync(Charge(key, 3m) with { AccountId = key }, ct);

        Assert.Equal(LedgerAppend.Inserted, first);
        Assert.Equal(LedgerAppend.Duplicate, second);
        Assert.Single(await store.ReadAsync(key, ct));
    }

    private static LedgerEntry Charge(string idempotencyKey, decimal amount) => new()
    {
        AccountId = "account",
        IdempotencyKey = idempotencyKey,
        Kind = LedgerKind.WalletCharge,
        Amount = amount,
        OccurredAt = DateTimeOffset.UnixEpoch,
    };

    private static LedgerEntry Cost(string idempotencyKey, decimal amount) => new()
    {
        AccountId = "account",
        IdempotencyKey = idempotencyKey,
        Kind = LedgerKind.PlatformCost,
        Amount = amount,
        OccurredAt = DateTimeOffset.UnixEpoch,
    };
}
