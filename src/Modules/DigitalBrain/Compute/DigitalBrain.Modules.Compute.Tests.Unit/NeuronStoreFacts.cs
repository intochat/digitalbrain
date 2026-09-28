using DigitalBrain.Compute;
using DigitalBrain.Compute.Ledger;
using DigitalBrain.Compute.Metering;
using DigitalBrain.Compute.Usage;
using DigitalBrain.Compute.Storage;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class NeuronStoreFacts
{
    [Fact]
    public async Task LegacyUsageImportsOnceWithItsTimestampRevisionAndSourceIntact()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var source = new LegacyUsage();
        var store = new NeuronUsageStore(brain.Grains, new LegacyComputeSources(Usage: source));
        var original = Assert.Single((await store.ReadAsync("a", "w", 10, null, ct)).Items);
        Assert.Equal(DateTimeOffset.UnixEpoch, original.OccurredAt);
        Assert.Equal("legacy", original.Payload);
        source.Unavailable = true;
        await store.AppendAsync("a", "w", "receipt", "stale", ct, DateTimeOffset.UnixEpoch);
        Assert.Equal("legacy", Assert.Single((await store.ReadAsync("a", "w", 10, null, ct)).Items).Payload);
        await store.AppendAsync("a", "w", "receipt", "current", ct);
        var reloaded = new NeuronUsageStore(brain.Grains, new LegacyComputeSources(Usage: source));
        Assert.Equal("current", Assert.Single((await reloaded.ReadAsync("a", "w", 10, null, ct)).Items).Payload);
        Assert.Equal(0, source.Writes);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task FailedLegacyReadBlocksNewChargesRatherThanResettingBalance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var store = new NeuronLedgerStore(brain.Grains, new LegacyComputeSources(Ledger: new UnavailableLedger()));
        await Assert.ThrowsAsync<IOException>(() => store.AppendAsync(new LedgerEntry
        {
            AccountId = "account", IdempotencyKey = "charge", Kind = LedgerKind.WalletCharge,
            Amount = 10, OccurredAt = DateTimeOffset.UtcNow,
        }, ct).AsTask());
        Assert.Empty(await new NeuronLedgerStore(brain.Grains, new LegacyComputeSources()).ReadAsync("account", ct));
    }
    [Fact]
    public async Task ImmutableTreeBoundsRecordsAndKeepsPublishedRootAfterFailedWrite()
    {
        var parts = new Dictionary<string, string>();
        var fail = false;
        var tree = new ComputeRecordTree(
            key => Task.FromResult(parts[key]),
            (key, value) => { if (fail) { throw new IOException("storage unavailable"); } parts[key] = value; return Task.CompletedTask; });
        string? root = null;
        for (var i = 0; i < 180; i++) { root = await tree.Set(root, new($"id-{i}", $"payload-{i}", i.ToString("D6"), i)); }
        var published = root;
        Assert.Equal("payload-17", (await tree.Get(published, "id-17"))!.Payload);
        var rows = new List<ComputeStoredRecord>();
        await foreach (var row in tree.Read(published)) { rows.Add(row); }
        Assert.Equal(180, rows.Count);
        Assert.All(parts.Values, value => Assert.True(value.Length < 128 * 1024));
        fail = true;
        await Assert.ThrowsAsync<IOException>(() => tree.Set(published, new("id-17", "lost", "000017", 200)));
        Assert.Equal("payload-17", (await tree.Get(published, "id-17"))!.Payload);
    }

    [Fact]
    public async Task NeuronRecordsSurviveDeactivationAndDeduplicateBatches()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var records = brain.Get<IComputeRecords>("test-records");
        var row = new ComputeStoredRecord("one", "original", "001", 10);
        Assert.Equal(1, await records.Put([row, row], false));
        await brain.DeactivateAsync(records, ct);
        Assert.Equal(0, await records.Put([row], false));
        Assert.Equal("original", Assert.Single(await records.Read()).Payload);
        await records.Put([row with { Payload = "stale", Revision = 1 }], true);
        Assert.Equal("original", Assert.Single(await records.Read()).Payload);
        await records.Put([row with { Payload = "updated", SortKey = "999", Revision = 11 }], true);
        var updated = Assert.Single(await records.Read());
        Assert.Equal("updated", updated.Payload);
        Assert.Equal("001", updated.SortKey);
    }
    [Fact]
    public async Task ProductionStoresUseGrainPersistenceWithoutDatabaseOrFilesystemFallback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        Assert.DoesNotContain("InMemory", brain.SiloServices.GetRequiredService<ILedgerStore>().GetType().Name);
        Assert.DoesNotContain("InMemory", brain.SiloServices.GetRequiredService<IMeterStore>().GetType().Name);
        Assert.DoesNotContain("File", brain.SiloServices.GetRequiredService<IUsageStore>().GetType().Name);
        var usage = brain.SiloServices.GetRequiredService<IUsageStore>();
        await usage.AppendAsync("account", "workspace", "first", "original", ct);
        var firstTime = (await usage.ReadAsync("account", "workspace", 1, null, ct)).Items[0].OccurredAt;
        await usage.AppendAsync("account", "workspace", "second", "second", ct);
        await usage.AppendAsync("account", "workspace", "first", "newer", ct);
        await usage.AppendAsync("account", "workspace", "first", "stale", ct, DateTimeOffset.UnixEpoch);
        var first = await usage.ReadAsync("account", "workspace", 1, null, ct);
        var second = await usage.ReadAsync("account", "workspace", 1, first.NextCursor, ct);
        Assert.Equal("second", Assert.Single(first.Items).Id);
        Assert.Equal("newer", Assert.Single(second.Items).Payload);
        Assert.Equal(firstTime, second.Items[0].OccurredAt);
        Assert.Null(second.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() => usage.ReadAsync("other", "workspace", 1, first.NextCursor, ct).AsTask());
    }
}

internal sealed class LegacyUsage : IUsageStore
{
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public bool Unavailable { get; set; }
    public ValueTask AppendAsync(string account, string workspace, string id, string payload, CancellationToken ct = default, DateTimeOffset? revision = null)
    { Writes++; throw new InvalidOperationException("Legacy source cannot be written."); }
    public ValueTask<UsagePage> ReadAsync(string account, string workspace, int limit, string? cursor, CancellationToken ct = default)
    {
        if (Unavailable) { throw new IOException("Legacy host removed."); }
        Reads++;
        return ValueTask.FromResult(new UsagePage([new("receipt", "legacy", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1).UtcTicks)], null));
    }
}

internal sealed class UnavailableLedger : ILedgerStore
{
    public ValueTask<decimal> ChargedAsync(string accountId, string? intentId, CancellationToken cancellationToken = default) => throw new IOException();
    public ValueTask<LedgerAppend> AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default) => throw new IOException();
    public ValueTask<IReadOnlyList<LedgerEntry>> ReadAsync(string accountId, CancellationToken cancellationToken = default) => throw new IOException();
}
