using DigitalBrain.Compute;
using DigitalBrain.Compute.Ledger;
using DigitalBrain.Compute.Metering;
using DigitalBrain.Compute.Storage;
using DigitalBrain.Compute.Usage;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Compute.Tests.Unit;

public sealed class NeuronStoreFacts
{
    [Fact]
    public async Task ANewAccountReadsAndWrites()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        var ledger = new NeuronLedgerStore(brain.Grains);
        Assert.Empty(await ledger.ReadAsync("account", ct));
        var appended = await ledger.AppendAsync(new LedgerEntry
        {
            AccountId = "account",
            IdempotencyKey = "charge",
            Kind = LedgerKind.WalletCharge,
            Amount = 10,
            OccurredAt = DateTimeOffset.UnixEpoch,
        }, ct);
        Assert.Equal(LedgerAppend.Inserted, appended);
        Assert.Equal(10m, Assert.Single(await ledger.ReadAsync("account", ct)).Amount);
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
