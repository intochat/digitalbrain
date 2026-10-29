using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

// The kit tests itself: each deliberately broken subject must fail the fact aimed at its fault, and the unbroken
// decorator must pass the same facts (so a failure is the fault, not the harness).
public sealed class BrokenStorageFacts
{
    [Fact]
    public Task Truncated_paging_fails_the_paging_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenKit(Fault.TruncatedPage).ListPagesCoverEveryItemExactlyOnce());

    [Fact]
    public Task Known_quota_without_total_fails_the_quota_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenKit(Fault.KnownQuotaWithoutTotal).DescribeReportsTriStateQuota());

    [Fact]
    public Task Ignored_range_fails_StreamReadRange() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenKit(Fault.IgnoreRange).StreamReadRange());

    [Fact]
    public Task Commit_on_dispose_fails_StreamWriteAbort() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenKit(Fault.CommitOnDispose).StreamWriteAbort());

    [Fact]
    public Task Overwrite_on_open_fails_StreamWriteConflict() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenKit(Fault.OverwriteOnOpen).StreamWriteConflict());

    [Fact]
    public Task Silent_overwrite_fails_the_write_conflict_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenKit(Fault.OverwriteOnOpen).WriteConflictFailsByDefault());

    [Fact]
    public async Task The_unbroken_decorator_passes_the_same_facts()
    {
        var kit = new BrokenKit(Fault.None);
        await kit.ListPagesCoverEveryItemExactlyOnce();
        await kit.DescribeReportsTriStateQuota();
        await kit.StreamReadRange();
        await kit.StreamWriteAbort();
        await kit.StreamWriteConflict();
        await kit.WriteConflictFailsByDefault();
    }

    internal enum Fault { None, TruncatedPage, KnownQuotaWithoutTotal, IgnoreRange, CommitOnDispose, OverwriteOnOpen }

    private sealed class BrokenKit(Fault fault) : StorageConformance<IStorage>
    {
        protected override Task<StorageSubject> Connect(StorageSeed seed, CancellationToken ct)
        {
            var inner = new InMemoryStorage("Broken");
            inner.Seed(seed);
            return Task.FromResult(new StorageSubject(new BrokenStorage(inner, fault), inner.Operations, InMemoryStorageConformance.Hostile));
        }
    }

    private sealed class BrokenStorage(InMemoryStorage inner, Fault fault) : IStorage
    {
        public async Task<StorageDescriptor> Describe(CancellationToken ct = default)
        {
            var descriptor = await inner.Describe(ct);
            // A Known quota with a null Total cannot even be constructed: the contract type rejects it, so the fact fails.
            return fault == Fault.KnownQuotaWithoutTotal ? descriptor with { Quota = new Quota(QuotaState.Known, 5, null) } : descriptor;
        }

        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => inner.Roots(ct);

        public async Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default)
        {
            var page = await inner.List(folder, query, ct);
            return fault == Fault.TruncatedPage ? page with { NextCursor = null } : page;
        }

        public Task<Item> Stat(ItemId id, CancellationToken ct = default) => inner.Stat(id, ct);

        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default) => inner.Read(id, range, ct);

        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default) =>
            inner.Write(fault == Fault.OverwriteOnOpen ? plan with { Conflict = ConflictPolicy.Overwrite } : plan, ct);

        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => inner.Search(query, ct);

        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => inner.Ancestors(id, ct);

        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) =>
            inner.OpenRead(item, fault == Fault.IgnoreRange ? null : range, ct);

        public async Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default) => fault switch
        {
            Fault.OverwriteOnOpen => await inner.OpenWrite(plan with { Conflict = ConflictPolicy.Overwrite }, ct),
            Fault.CommitOnDispose => new CommitAlways(inner, plan),
            _ => await inner.OpenWrite(plan, ct),
        };
    }

    // Commits even when aborted or disposed: an uncommitted write becomes visible.
    private sealed class CommitAlways(InMemoryStorage inner, WritePlan plan) : StorageWriteStream(new MemoryStream())
    {
        protected override async Task<ItemId> CommitCore(CancellationToken ct)
        {
            await Save(ct);
            var page = await inner.List(plan.TargetFolder!.Value, new ListQuery(PageSize: ListQuery.MaxPageSize), ct);
            return page.Items.Single(item => item.Name == plan.Name).Id;
        }

        protected override Task AbortCore(CancellationToken ct) => Save(ct);

        private async Task Save(CancellationToken ct)
        {
            var bytes = ((MemoryStream)Content).ToArray();
            var id = await inner.Write(WritePlan.WriteFile(plan.TargetFolder!.Value, plan.Name!, bytes), ct);
            _ = await inner.Operations.WaitFor(id, ct);
        }
    }
}
