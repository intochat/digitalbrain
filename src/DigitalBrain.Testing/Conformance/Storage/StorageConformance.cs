using System.Text;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using Xunit;

namespace DigitalBrain.Testing.Conformance.Storage;

/// <summary>A connected storage under test.</summary>
/// <param name="Storage">The storage connection, already holding the <see cref="StorageSeed"/> tree.</param>
/// <param name="Operations">Waits for the operations <see cref="IStorage.Write"/> starts.</param>
/// <param name="HostileIds">Ids outside the connection's confinement (escaping paths, foreign roots); each must fail with <c>denied</c>. Empty skips the confinement fact.</param>
public sealed record StorageSubject(IStorage Storage, IOperationsProbe Operations, IReadOnlyList<ItemId>? HostileIds = null);

/// <summary>
/// The storage@1 conformance kit (CAP-01, CAP-04). A module providing storage subclasses it in its <c>tests/</c> project
/// as a sealed class and implements <see cref="Connect"/>; every inherited fact runs against that subject. Facts for
/// optional actions skip, with a reason, only when <see cref="StorageDescriptor.Flags"/> does not offer the action.
/// Assertion messages carry ids and names only, never content bytes.
/// </summary>
/// <typeparam name="TStorage">The storage implementation under test (names the subclass's subject; not otherwise used).</typeparam>
public abstract class StorageConformance<TStorage> where TStorage : IStorage
{
    /// <summary>Every fact's timeout in milliseconds.</summary>
    protected const int Timeout = 30_000;

    private const int MaxAncestors = 32;

    /// <summary>
    /// Connects to a storage whose seed folder (see <see cref="SeedFolder"/>) holds <paramref name="seed"/>. Called once per fact;
    /// a fact may call it twice (cross-connection move), so each call should give an independent seeded folder.
    /// </summary>
    protected abstract Task<StorageSubject> Connect(StorageSeed seed, CancellationToken ct);

    /// <summary>An id the storage does not know; not-found is expected. Override with a module-shaped id.</summary>
    protected virtual ItemId MissingId() => new($"kit-missing-{Guid.NewGuid():N}");

    /// <summary>The folder holding the seed; the first root by default.</summary>
    protected virtual async Task<ItemId> SeedFolder(IStorage storage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var roots = await storage.Roots(ct).ConfigureAwait(false);
        Assert.NotEmpty(roots);
        return roots[0].Id;
    }

    // ----- Describe, roots -----

    /// <summary>Quota is tri-state: Known has Used and Total with Total &gt;= Used; Unknown has no numbers; Unlimited has no Total.</summary>
    [Fact(Timeout = Timeout)]
    public async Task DescribeReportsTriStateQuota()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var descriptor = await s.Storage.Describe(ct).ConfigureAwait(false);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Label), "the descriptor has no label");
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Kind), "the descriptor has no kind");
        var quota = descriptor.Quota;
        Assert.NotNull(quota);
        switch (quota.State)
        {
            case QuotaState.Known:
                Assert.NotNull(quota.Used);
                Assert.NotNull(quota.Total);
                Assert.True(quota.Total >= quota.Used, "known quota total is below used");
                break;
            case QuotaState.Unknown:
                Assert.Null(quota.Used);
                Assert.Null(quota.Total);
                break;
            case QuotaState.Unlimited:
                Assert.Null(quota.Total);
                break;
            default:
                Assert.Fail($"undefined quota state {quota.State}");
                break;
        }
    }

    /// <summary>There is at least one root and every root stats to a folder.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RootsAreNonEmptyAndStatable()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var roots = await s.Storage.Roots(ct).ConfigureAwait(false);
        Assert.NotEmpty(roots);
        foreach (var root in roots)
        {
            var stat = await s.Storage.Stat(root.Id, ct).ConfigureAwait(false);
            Assert.Equal(ItemKind.Folder, stat.Kind);
            Assert.Equal(root.Id, stat.Id);
        }
    }

    // ----- List -----

    /// <summary>Walking the cursor over the 250-item folder with page size 100 yields 250 distinct ids in three pages.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ListPagesCoverEveryItemExactlyOnce()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var many = await Child(s.Storage, await SeedFolder(s.Storage, ct).ConfigureAwait(false), StorageSeed.ManyFolder, ct).ConfigureAwait(false);
        var ids = new List<ItemId>();
        var names = new List<string>();
        var pages = 0;
        string? cursor = null;
        do
        {
            var page = await s.Storage.List(many.Id, new ListQuery(cursor, 100), ct).ConfigureAwait(false);
            Assert.True(page.Items.Count <= 100, $"page {pages} holds {page.Items.Count} items, above the page size");
            ids.AddRange(page.Items.Select(item => item.Id));
            names.AddRange(page.Items.Select(item => item.Name));
            cursor = page.NextCursor;
            pages++;
            Assert.True(pages <= 10, "the cursor never ends");
        }
        while (cursor is not null);

        Assert.Equal(StorageSeed.ManyCount, ids.Count);
        Assert.Equal(StorageSeed.ManyCount, ids.Distinct().Count());
        Assert.Equal(3, pages);
        Assert.Equal(Enumerable.Range(0, StorageSeed.ManyCount).Select(StorageSeed.ManyName).Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    /// <summary>The default sort is folders first, then names ordinal-ignore-case ascending; Descending reverses it.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ListIsSortedFolderThenName()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var ascending = await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false);
        var expected = ascending.OrderBy(item => item.Kind == ItemKind.Folder ? 0 : 1).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => item.Name).ToList();
        Assert.Equal(expected, ascending.Select(item => item.Name));
        Assert.Contains(StorageSeed.PlanName, expected);
        Assert.Contains(StorageSeed.NotesFolder, expected);

        var descending = await All(s.Storage, folder, new ListQuery(Descending: true), ct).ConfigureAwait(false);
        expected.Reverse();
        Assert.Equal(expected, descending.Select(item => item.Name));
    }

    /// <summary>Listed items report kind, size for files and a modification time; the seed folder lists the seeded children.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ListReportsKindsAndSizes()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var items = await All(s.Storage, await SeedFolder(s.Storage, ct).ConfigureAwait(false), new ListQuery(), ct).ConfigureAwait(false);
        var plan = Assert.Single(items, item => item.Name == StorageSeed.PlanName);
        Assert.Equal(ItemKind.File, plan.Kind);
        Assert.Equal(StorageSeed.PlanBytes.Length, plan.Size);
        foreach (var name in new[] { StorageSeed.NotesFolder, StorageSeed.ManyFolder, StorageSeed.DeepChain[0] })
        {
            Assert.Equal(ItemKind.Folder, Assert.Single(items, item => item.Name == name).Kind);
        }
    }

    // ----- Stat, ids, flags -----

    /// <summary>Stat of the seeded file reports its name, kind, size and modification time.</summary>
    [Fact(Timeout = Timeout)]
    public async Task StatReportsNameKindAndSize()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        var stat = await s.Storage.Stat(plan.Id, ct).ConfigureAwait(false);
        Assert.Equal(StorageSeed.PlanName, stat.Name);
        Assert.Equal(ItemKind.File, stat.Kind);
        Assert.Equal(StorageSeed.PlanBytes.Length, stat.Size);
    }

    /// <summary>An id from List stats to the same name and kind (ids are opaque but stable).</summary>
    [Fact(Timeout = Timeout)]
    public async Task ItemIdsRoundTrip()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var items = await All(s.Storage, await SeedFolder(s.Storage, ct).ConfigureAwait(false), new ListQuery(), ct).ConfigureAwait(false);
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            Assert.False(string.IsNullOrEmpty(item.Id.Value), $"{item.Name} has an empty id");
            var stat = await s.Storage.Stat(item.Id, ct).ConfigureAwait(false);
            Assert.Equal(item.Name, stat.Name);
            Assert.Equal(item.Kind, stat.Kind);
        }
    }

    /// <summary>Item flags stay within what the storage offers, and a readable storage marks files readable.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ItemFlagsStayWithinTheDescriptor()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var descriptor = await s.Storage.Describe(ct).ConfigureAwait(false);
        var offered = descriptor.Flags | ItemFlags.Shared | ItemFlags.CloudOnly;
        var items = await All(s.Storage, await SeedFolder(s.Storage, ct).ConfigureAwait(false), new ListQuery(), ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            Assert.True((item.Flags & ~offered) == ItemFlags.None, $"{item.Name} has flags {item.Flags} beyond the storage's {descriptor.Flags}");
        }
        if (descriptor.Flags.HasFlag(ItemFlags.Read))
        {
            Assert.True(Assert.Single(items, item => item.Name == StorageSeed.PlanName).Flags.HasFlag(ItemFlags.Read), "Plan.txt is not readable");
        }
    }

    /// <summary>Stat of an unknown id throws <see cref="StorageException"/> with code <c>not-found</c>.</summary>
    [Fact(Timeout = Timeout)]
    public async Task UnknownIdIsNotFound()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var missing = MissingId();
        var error = await Assert.ThrowsAnyAsync<StorageException>(() => s.Storage.Stat(missing, ct)).ConfigureAwait(false);
        Assert.Equal("not-found", error.Code);
    }

    /// <summary>Every subclass-supplied hostile id throws code <c>denied</c> from Stat, List and Read (confinement, CAP-05 entry point).</summary>
    [Fact(Timeout = Timeout)]
    public async Task OutsideConfinementIsDenied()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var hostile = s.HostileIds ?? [];
        Assert.SkipWhen(hostile.Count == 0, $"{typeof(TStorage).Name} supplies no hostile ids");
        foreach (var id in hostile)
        {
            foreach (var call in new Func<Task>[] { () => s.Storage.Stat(id, ct), () => s.Storage.List(id, new ListQuery(), ct), () => s.Storage.Read(id, null, ct) })
            {
                var error = await Assert.ThrowsAnyAsync<StorageException>(call).ConfigureAwait(false);
                Assert.True(error.Code == "denied", $"hostile id {id.Value} gave {error.Code}, not denied");
            }
        }
    }

    // ----- Read -----

    /// <summary>A full read equals the seeded bytes; range (2, 3) returns exactly 3 bytes from offset 2; ReadText decodes the UTF-8 note.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ReadReturnsBytesAndRange()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        var whole = await s.Storage.Read(plan.Id, null, ct).ConfigureAwait(false);
        Assert.True(whole.Bytes.Span.SequenceEqual(StorageSeed.PlanBytes.Span), "Plan.txt bytes differ from the seed");
        Assert.False(string.IsNullOrWhiteSpace(whole.MediaType), "no media type");

        var range = await s.Storage.Read(plan.Id, new ByteRange(2, 3), ct).ConfigureAwait(false);
        Assert.True(range.Bytes.Span.SequenceEqual(StorageSeed.PlanBytes.Span.Slice(2, 3)), "range (2, 3) of Plan.txt differs");
        if (range.TotalLength is { } total) { Assert.Equal(StorageSeed.PlanBytes.Length, total); }

        var notes = await Child(s.Storage, await SeedFolder(s.Storage, ct).ConfigureAwait(false), StorageSeed.NotesFolder, ct).ConfigureAwait(false);
        var note = await Child(s.Storage, notes.Id, StorageSeed.NoteName, ct).ConfigureAwait(false);
        Assert.Equal(StorageSeed.NoteText, await s.Storage.ReadText(note.Id, ct).ConfigureAwait(false));
    }

    // ----- Ancestors, Search -----

    /// <summary>The ancestor chain of the deepest seeded file starts at a root, ends at its folder, has no repeated id and at most 32 entries.</summary>
    [Fact(Timeout = Timeout)]
    public async Task AncestorsEndAtARootWithoutCycles()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        Item? parent = null;
        foreach (var name in StorageSeed.DeepChain)
        {
            parent = await Child(s.Storage, parent?.Id ?? folder, name, ct).ConfigureAwait(false);
        }
        var leaf = await Child(s.Storage, parent!.Id, StorageSeed.LeafName, ct).ConfigureAwait(false);

        var chain = await s.Storage.Ancestors(leaf.Id, ct).ConfigureAwait(false);
        var roots = (await s.Storage.Roots(ct).ConfigureAwait(false)).Select(root => root.Id).ToHashSet();
        Assert.NotEmpty(chain);
        Assert.True(chain.Count <= MaxAncestors, $"ancestor chain of {leaf.Id.Value} has {chain.Count} entries");
        Assert.Equal(chain.Count, chain.Select(item => item.Id).Distinct().Count());
        Assert.Contains(chain[0].Id, roots);
        Assert.Equal(parent.Id, chain[^1].Id);
        Assert.DoesNotContain(chain, item => item.Id == leaf.Id);
    }

    /// <summary>Search by name finds the seeded <c>Plan.txt</c>.</summary>
    [Fact(Timeout = Timeout)]
    public async Task SearchByNameFindsSeededFile()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Search, ct).ConfigureAwait(false);
        var found = new List<Item>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await s.Storage.Search(new SearchQuery("plan", Cursor: cursor), ct).ConfigureAwait(false);
            found.AddRange(page.Items);
            cursor = page.NextCursor;
            Assert.True(++pages <= 20, "the search cursor never ends");
        }
        while (cursor is not null);
        Assert.Contains(found, item => item.Name == StorageSeed.PlanName && item.Kind == ItemKind.File);
    }

    // ----- Write -----

    /// <summary>An explicitly replacing rename leaves one destination with the source bytes, never duplicate names.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RenameOverwriteReplacesExactlyOneDestination()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Write | ItemFlags.Rename, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var sourceName = Unique("staged") + ".txt";
        var targetName = Unique("target") + ".txt";
        await Succeeds(s, WritePlan.WriteFile(folder, sourceName, Encoding.UTF8.GetBytes("verified replacement")), ct).ConfigureAwait(false);
        await Succeeds(s, WritePlan.WriteFile(folder, targetName, Encoding.UTF8.GetBytes("original")), ct).ConfigureAwait(false);
        var source = await Child(s.Storage, folder, sourceName, ct).ConfigureAwait(false);
        await Succeeds(s, WritePlan.Rename(source.Id, targetName, ConflictPolicy.Overwrite), ct).ConfigureAwait(false);
        var items = await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false);
        Assert.DoesNotContain(items, item => item.Name == sourceName);
        var target = Assert.Single(items, item => item.Name == targetName);
        Assert.Equal("verified replacement", await s.Storage.ReadText(target.Id, ct).ConfigureAwait(false));
    }

    /// <summary>CreateFolder succeeds and the new folder stats as a folder.</summary>
    [Fact(Timeout = Timeout)]
    public async Task CreateFolderThenStat()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.CreateFolder, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var name = Unique("folder");
        await Succeeds(s, WritePlan.CreateFolder(folder, name), ct).ConfigureAwait(false);
        var created = await Child(s.Storage, folder, name, ct).ConfigureAwait(false);
        Assert.Equal(ItemKind.Folder, (await s.Storage.Stat(created.Id, ct).ConfigureAwait(false)).Kind);
    }

    /// <summary>WriteFile with inline content succeeds and reading the new file returns the bytes.</summary>
    [Fact(Timeout = Timeout)]
    public async Task WriteFileThenRead()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Write, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var name = Unique("write") + ".txt";
        var bytes = Encoding.UTF8.GetBytes("written by the kit");
        await Succeeds(s, WritePlan.WriteFile(folder, name, bytes), ct).ConfigureAwait(false);
        var written = await Child(s.Storage, folder, name, ct).ConfigureAwait(false);
        Assert.True((await s.Storage.Read(written.Id, null, ct).ConfigureAwait(false)).Bytes.Span.SequenceEqual(bytes), $"{name} reads back different bytes");
    }

    /// <summary>Rename changes the name, keeps the content, and the old name is gone.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RenameKeepsContent()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Rename, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        var name = Unique("renamed") + ".txt";
        await Succeeds(s, WritePlan.Rename(plan.Id, name), ct).ConfigureAwait(false);
        var names = (await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false)).Select(item => item.Name).ToList();
        Assert.Contains(name, names);
        Assert.DoesNotContain(StorageSeed.PlanName, names);
        var renamed = await Child(s.Storage, folder, name, ct).ConfigureAwait(false);
        Assert.True((await s.Storage.Read(renamed.Id, null, ct).ConfigureAwait(false)).Bytes.Span.SequenceEqual(StorageSeed.PlanBytes.Span), "renamed file content changed");
    }

    /// <summary>Copy puts an equal file in the target folder and leaves the source.</summary>
    [Fact(Timeout = Timeout)]
    public async Task CopyLeavesSource()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Copy, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        var notes = await Child(s.Storage, folder, StorageSeed.NotesFolder, ct).ConfigureAwait(false);
        await Succeeds(s, WritePlan.Copy(plan.Id, notes.Id), ct).ConfigureAwait(false);
        var copy = await Child(s.Storage, notes.Id, StorageSeed.PlanName, ct).ConfigureAwait(false);
        Assert.NotEqual(plan.Id, copy.Id);
        Assert.True((await s.Storage.Read(copy.Id, null, ct).ConfigureAwait(false)).Bytes.Span.SequenceEqual(StorageSeed.PlanBytes.Span), "the copy differs");
        Assert.Equal(StorageSeed.PlanName, (await s.Storage.Stat(plan.Id, ct).ConfigureAwait(false)).Name);
    }

    /// <summary>Move puts the item in the target folder and removes it from the source folder.</summary>
    [Fact(Timeout = Timeout)]
    public async Task MoveRemovesSource()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Move, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        var notes = await Child(s.Storage, folder, StorageSeed.NotesFolder, ct).ConfigureAwait(false);
        await Succeeds(s, WritePlan.Move(plan.Id, notes.Id), ct).ConfigureAwait(false);
        Assert.DoesNotContain(await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false), item => item.Name == StorageSeed.PlanName);
        var moved = await Child(s.Storage, notes.Id, StorageSeed.PlanName, ct).ConfigureAwait(false);
        Assert.True((await s.Storage.Read(moved.Id, null, ct).ConfigureAwait(false)).Bytes.Span.SequenceEqual(StorageSeed.PlanBytes.Span), "the moved file differs");
    }

    /// <summary>Delete without Permanent moves the item to the trash: it is no longer listed in its folder.</summary>
    [Fact(Timeout = Timeout)]
    public async Task DeleteMovesToTrash()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Trash, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        var delete = WritePlan.Delete(plan.Id);
        Assert.False(delete.Permanent, "delete must default to the trash");
        await Succeeds(s, delete, ct).ConfigureAwait(false);
        Assert.DoesNotContain(await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false), item => item.Id == plan.Id || item.Name == StorageSeed.PlanName);
    }

    /// <summary>Delete with Permanent removes the item: it is not listed and Stat is not-found.</summary>
    [Fact(Timeout = Timeout)]
    public async Task DeletePermanentRemoves()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Delete, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        await Succeeds(s, WritePlan.Delete(plan.Id, permanent: true), ct).ConfigureAwait(false);
        Assert.DoesNotContain(await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false), item => item.Name == StorageSeed.PlanName);
        var error = await Assert.ThrowsAnyAsync<StorageException>(() => s.Storage.Stat(plan.Id, ct)).ConfigureAwait(false);
        Assert.Equal("not-found", error.Code);
    }

    /// <summary>A second WriteFile with an existing name and the default policy fails with code <c>conflict</c> and leaves the original bytes.</summary>
    [Fact(Timeout = Timeout)]
    public async Task WriteConflictFailsByDefault()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Write, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var plan = WritePlan.WriteFile(folder, StorageSeed.PlanName, "overwritten"u8.ToArray());
        Assert.Equal(ConflictPolicy.Fail, plan.Conflict);
        var result = await Run(s, plan, ct).ConfigureAwait(false);
        var failed = Assert.IsType<OperationResult.Failed>(result);
        Assert.Equal("conflict", failed.Code);
        var original = await Plan(s, ct).ConfigureAwait(false);
        Assert.True((await s.Storage.Read(original.Id, null, ct).ConfigureAwait(false)).Bytes.Span.SequenceEqual(StorageSeed.PlanBytes.Span), "Plan.txt was overwritten");
    }

    /// <summary>A dry run completes with Planned steps and changes nothing.</summary>
    [Fact(Timeout = Timeout)]
    public async Task DryRunChangesNothing()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.CreateFolder, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var before = (await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false)).Select(item => item.Name).ToList();
        var name = Unique("dry");
        var result = await Run(s, WritePlan.CreateFolder(folder, name) with { DryRun = true }, ct).ConfigureAwait(false);
        var planned = Assert.IsType<OperationResult.Planned>(result);
        Assert.NotEmpty(planned.Steps);
        var after = (await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false)).Select(item => item.Name).ToList();
        Assert.Equal(before, after);
    }

    /// <summary>
    /// A move across two connections is copy, verify, then delete: the bytes streamed out of one connection into the other
    /// read back equal before the source is deleted, and afterwards only the target holds the file.
    /// </summary>
    [Fact(Timeout = Timeout)]
    public async Task CrossConnectionMoveCopiesVerifiesThenDeletes()
    {
        var (source, ct) = await Open().ConfigureAwait(false);
        var target = await Connect(StorageSeed.Canonical, ct).ConfigureAwait(false);
        await Require(target, ItemFlags.Write, ct).ConfigureAwait(false);
        var sourceFlags = (await source.Storage.Describe(ct).ConfigureAwait(false)).Flags;
        Assert.SkipUnless(sourceFlags.HasFlag(ItemFlags.Trash) || sourceFlags.HasFlag(ItemFlags.Delete), $"{typeof(TStorage).Name} offers neither Trash nor Delete");

        var plan = await Plan(source, ct).ConfigureAwait(false);
        var targetFolder = await SeedFolder(target.Storage, ct).ConfigureAwait(false);
        var name = Unique("moved") + ".txt";
        ItemId copied;
        await using (var read = await source.Storage.OpenRead(plan.Id, null, ct).ConfigureAwait(false))
        {
            await using var write = await target.Storage.OpenWrite(WritePlan.WriteFile(targetFolder, name), ct).ConfigureAwait(false);
            await read.Content.CopyToAsync(write.Content, ct).ConfigureAwait(false);
            copied = await write.Commit(ct).ConfigureAwait(false);
        }
        Assert.True((await target.Storage.Read(copied, null, ct).ConfigureAwait(false)).Bytes.Span.SequenceEqual(StorageSeed.PlanBytes.Span), "the copy does not verify");

        await Succeeds(source, WritePlan.Delete(plan.Id, permanent: !sourceFlags.HasFlag(ItemFlags.Trash)), ct).ConfigureAwait(false);
        Assert.DoesNotContain(await All(source.Storage, await SeedFolder(source.Storage, ct).ConfigureAwait(false), new ListQuery(), ct).ConfigureAwait(false), item => item.Name == StorageSeed.PlanName);
        Assert.Equal(name, (await target.Storage.Stat(copied, ct).ConfigureAwait(false)).Name);
    }

    // ----- Streams -----

    /// <summary>OpenRead streams all 16 bytes of Plan.txt with Length 16 and a content type; range (2, 10) streams exactly 10 bytes from offset 2.</summary>
    [Fact(Timeout = Timeout)]
    public async Task StreamReadRange()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        await using (var whole = await s.Storage.OpenRead(plan.Id, null, ct).ConfigureAwait(false))
        {
            Assert.Equal(StorageSeed.PlanBytes.Length, whole.Length);
            Assert.False(string.IsNullOrWhiteSpace(whole.ContentType), "no content type");
            _ = whole.CanSeek; // reported either way; a non-seekable storage still serves ranges
            Assert.True((await Drain(whole.Content, ct).ConfigureAwait(false)).AsSpan().SequenceEqual(StorageSeed.PlanBytes.Span), "streamed bytes differ");
        }
        await using var range = await s.Storage.OpenRead(plan.Id, new ByteRange(2, 10), ct).ConfigureAwait(false);
        if (range.Length is { } length) { Assert.Equal(10, length); }
        var bytes = await Drain(range.Content, ct).ConfigureAwait(false);
        Assert.Equal(10, bytes.Length);
        Assert.True(bytes.AsSpan().SequenceEqual(StorageSeed.PlanBytes.Span.Slice(2, 10)), "streamed range differs");
    }

    /// <summary>OpenWrite then Commit makes a file that stats and reads back the same bytes; before Commit the name is not listed.</summary>
    [Fact(Timeout = Timeout)]
    public async Task StreamWriteCommit()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Write, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var name = Unique("stream") + ".bin";
        var bytes = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
        await using var write = await s.Storage.OpenWrite(WritePlan.WriteFile(folder, name), ct).ConfigureAwait(false);
        await write.Content.WriteAsync(bytes, ct).ConfigureAwait(false);
        await write.Content.FlushAsync(ct).ConfigureAwait(false);
        Assert.DoesNotContain(await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false), item => item.Name == name);

        var id = await write.Commit(ct).ConfigureAwait(false);
        var stat = await s.Storage.Stat(id, ct).ConfigureAwait(false);
        Assert.Equal(name, stat.Name);
        Assert.Equal(bytes.Length, stat.Size);
        Assert.True((await s.Storage.Read(id, null, ct).ConfigureAwait(false)).Bytes.Span.SequenceEqual(bytes), $"{name} reads back different bytes");
    }

    /// <summary>Abort, and separately dispose without Commit, leave no item with the name.</summary>
    [Fact(Timeout = Timeout)]
    public async Task StreamWriteAbort()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Write, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);

        var aborted = Unique("aborted") + ".txt";
        await using (var write = await s.Storage.OpenWrite(WritePlan.WriteFile(folder, aborted), ct).ConfigureAwait(false))
        {
            await write.Content.WriteAsync("partial"u8.ToArray(), ct).ConfigureAwait(false);
            await write.Abort(ct).ConfigureAwait(false);
        }

        var disposed = Unique("disposed") + ".txt";
        var handle = await s.Storage.OpenWrite(WritePlan.WriteFile(folder, disposed), ct).ConfigureAwait(false);
        await handle.Content.WriteAsync("partial"u8.ToArray(), ct).ConfigureAwait(false);
        await handle.DisposeAsync().ConfigureAwait(false);

        var names = (await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false)).Select(item => item.Name).ToList();
        Assert.DoesNotContain(aborted, names);
        Assert.DoesNotContain(disposed, names);
    }

    /// <summary>OpenWrite into an existing name with the default policy throws code <c>conflict</c> before any byte is accepted.</summary>
    [Fact(Timeout = Timeout)]
    public async Task StreamWriteConflict()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await Require(s, ItemFlags.Write, ct).ConfigureAwait(false);
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var error = await Assert.ThrowsAnyAsync<StorageException>(async () =>
        {
            await using var write = await s.Storage.OpenWrite(WritePlan.WriteFile(folder, StorageSeed.PlanName), ct).ConfigureAwait(false);
            await write.Content.WriteAsync("overwritten"u8.ToArray(), ct).ConfigureAwait(false);
            _ = await write.Commit(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
        Assert.Equal("conflict", error.Code);
        var plan = await Plan(s, ct).ConfigureAwait(false);
        Assert.True((await s.Storage.Read(plan.Id, null, ct).ConfigureAwait(false)).Bytes.Span.SequenceEqual(StorageSeed.PlanBytes.Span), "Plan.txt was overwritten");
    }

    /// <summary>A connection that does not offer Write throws code <c>unsupported</c> from OpenWrite and creates nothing.</summary>
    [Fact(Timeout = Timeout)]
    public async Task StreamWriteUnsupportedWithoutWrite()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var flags = (await s.Storage.Describe(ct).ConfigureAwait(false)).Flags;
        Assert.SkipWhen(flags.HasFlag(ItemFlags.Write), $"{typeof(TStorage).Name} offers Write");
        var folder = await SeedFolder(s.Storage, ct).ConfigureAwait(false);
        var name = Unique("readonly") + ".txt";
        var error = await Assert.ThrowsAnyAsync<StorageException>(async () =>
        {
            await using var write = await s.Storage.OpenWrite(WritePlan.WriteFile(folder, name), ct).ConfigureAwait(false);
            await write.Content.WriteAsync("nope"u8.ToArray(), ct).ConfigureAwait(false);
            _ = await write.Commit(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
        Assert.Equal("unsupported", error.Code);
        Assert.DoesNotContain(await All(s.Storage, folder, new ListQuery(), ct).ConfigureAwait(false), item => item.Name == name);
    }

    // ----- helpers -----

    private async Task<(StorageSubject Subject, CancellationToken Ct)> Open()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = await Connect(StorageSeed.Canonical, ct).ConfigureAwait(false);
        Assert.NotNull(subject);
        Assert.NotNull(subject.Storage);
        Assert.NotNull(subject.Operations);
        return (subject, ct);
    }

    private static async Task Require(StorageSubject subject, ItemFlags flag, CancellationToken ct)
    {
        var flags = (await subject.Storage.Describe(ct).ConfigureAwait(false)).Flags;
        Assert.SkipUnless(flags.HasFlag(flag), $"{typeof(TStorage).Name} does not offer {flag}");
    }

    private async Task<Item> Plan(StorageSubject subject, CancellationToken ct) =>
        await Child(subject.Storage, await SeedFolder(subject.Storage, ct).ConfigureAwait(false), StorageSeed.PlanName, ct).ConfigureAwait(false);

    private static async Task<Item> Child(IStorage storage, ItemId folder, string name, CancellationToken ct)
    {
        var items = await All(storage, folder, new ListQuery(), ct).ConfigureAwait(false);
        var match = items.Where(item => item.Name == name).ToList();
        Assert.True(match.Count == 1, $"folder {folder.Value} holds {match.Count} items named {name}");
        return match[0];
    }

    private static async Task<List<Item>> All(IStorage storage, ItemId folder, ListQuery query, CancellationToken ct)
    {
        var items = new List<Item>();
        var cursor = query.Cursor;
        for (var pages = 0; ; pages++)
        {
            Assert.True(pages < 100, $"listing {folder.Value} never ends");
            var page = await storage.List(folder, query with { Cursor = cursor }, ct).ConfigureAwait(false);
            items.AddRange(page.Items);
            cursor = page.NextCursor;
            if (cursor is null) { return items; }
        }
    }

    // Runs a write to its end; a storage that rejects the plan synchronously reports the same code as a failed result.
    private static async Task<OperationResult> Run(StorageSubject subject, WritePlan plan, CancellationToken ct)
    {
        OperationId id;
        try
        {
            id = await subject.Storage.Write(plan, ct).ConfigureAwait(false);
        }
        catch (StorageException error)
        {
            return new OperationResult.Failed(error.Code, error.Message);
        }
        return await subject.Operations.WaitFor(id, ct).ConfigureAwait(false);
    }

    private static async Task Succeeds(StorageSubject subject, WritePlan plan, CancellationToken ct)
    {
        var result = await Run(subject, plan, ct).ConfigureAwait(false);
        Assert.True(result is OperationResult.Succeeded, $"{plan.Kind} {plan.Name} ended {result}");
    }

    private static async Task<byte[]> Drain(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static string Unique(string prefix) => $"kit-{prefix}-{Guid.NewGuid():N}";
}
