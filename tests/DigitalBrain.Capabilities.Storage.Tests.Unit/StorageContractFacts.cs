using System.Reflection;
using System.Text.Json;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;

namespace DigitalBrain.Capabilities.Storage.Tests.Unit;

public sealed class QuotaFacts
{
    [Fact]
    public void Known_carries_used_and_total()
    {
        var quota = Quota.Known(10, 100);
        Assert.Equal(QuotaState.Known, quota.State);
        Assert.Equal(10, quota.Used);
        Assert.Equal(100, quota.Total);
        Assert.False(quota.Pooled);
    }

    [Fact]
    public void Unlimited_has_no_total()
    {
        var quota = Quota.Unlimited(10);
        Assert.Equal(QuotaState.Unlimited, quota.State);
        Assert.Equal(10, quota.Used);
        Assert.Null(quota.Total);
    }

    [Fact]
    public void Unknown_is_never_zero()
    {
        Assert.Equal(QuotaState.Unknown, Quota.Unknown.State);
        Assert.Null(Quota.Unknown.Used);
        Assert.Null(Quota.Unknown.Total);
    }

    [Theory]
    [InlineData(101, 100)]
    [InlineData(-1, 100)]
    [InlineData(0, -1)]
    public void Known_rejects_impossible_values(long used, long total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Quota.Known(used, total));
    }

    [Fact]
    public void States_reject_inconsistent_numbers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quota(QuotaState.Unknown, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quota(QuotaState.Known, null, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quota(QuotaState.Unlimited, 1, 10));
    }

    [Fact]
    public void Json_round_trip_keeps_state()
    {
        foreach (var quota in new[] { Quota.Known(1, 2, pooled: true), Quota.Unlimited(5), Quota.Unknown })
        {
            var back = JsonSerializer.Deserialize<Quota>(JsonSerializer.Serialize(quota));
            Assert.Equal(quota, back);
        }
        Assert.NotEqual(Quota.Unlimited(0).State, Quota.Unknown.State);
    }
}

public sealed class ItemIdFacts
{
    [Fact]
    public void Equality_is_ordinal_on_value()
    {
        Assert.Equal(new ItemId("a/B"), new ItemId("a/B"));
        Assert.NotEqual(new ItemId("a/B"), new ItemId("a/b"));
    }

    [Fact]
    public void Is_opaque()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        Assert.Equal(["Value"], typeof(ItemId).GetProperties(all).Select(p => p.Name));
        var methods = typeof(ItemId).GetMethods(all)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .Where(n => n is not ("ToString" or "Equals" or "GetHashCode" or "Deconstruct"))
            .ToList();
        Assert.Empty(methods);
    }
}

public sealed class ListQueryFacts
{
    [Fact]
    public void Defaults_are_folder_then_name_hundred()
    {
        var query = new ListQuery();
        Assert.Equal(100, query.PageSize);
        Assert.Equal(SortKey.FolderThenName, query.Sort);
        Assert.False(query.Descending);
        Assert.Null(query.Cursor);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, 1000)]
    [InlineData(250, 250)]
    public void Page_size_is_clamped(int asked, int expected)
    {
        Assert.Equal(expected, new ListQuery(PageSize: asked).PageSize);
        Assert.Equal(expected, new SearchQuery("x", PageSize: asked).PageSize);
        Assert.Equal(expected, (new ListQuery() with { PageSize = asked }).PageSize);
    }

    [Fact]
    public void Undefined_sort_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ListQuery(Sort: (SortKey)99));
    }

    [Fact]
    public void Search_defaults_to_name_mode_and_rejects_empty_text()
    {
        Assert.Equal(SearchMode.Name, new SearchQuery("x").Mode);
        Assert.Throws<ArgumentException>(() => new SearchQuery(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery("x", (SearchMode)7));
    }
}

public sealed class WritePlanFacts
{
    private static readonly ItemId Folder = new("f");
    private static readonly ItemId File = new("x");

    public static TheoryData<WritePlan> Factories() => new()
    {
        WritePlan.CreateFolder(Folder, "n"),
        WritePlan.WriteFile(Folder, "n", new byte[] { 1 }),
        WritePlan.Move(File, Folder),
        WritePlan.Copy(File, Folder),
        WritePlan.Rename(File, "m"),
        WritePlan.Delete(File),
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void Every_factory_defaults_conflict_to_fail(WritePlan plan)
    {
        Assert.Equal(ConflictPolicy.Fail, plan.Conflict);
        Assert.False(plan.DryRun);
    }

    [Fact]
    public void Delete_defaults_to_trash()
    {
        var plan = WritePlan.Delete(File);
        Assert.Equal(WriteKind.Delete, plan.Kind);
        Assert.False(plan.Permanent);
        Assert.True(WritePlan.Delete(File, permanent: true).Permanent);
    }

    [Fact]
    public void Overwrite_only_by_explicit_argument()
    {
        Assert.Equal(ConflictPolicy.Overwrite, WritePlan.Copy(File, Folder, ConflictPolicy.Overwrite).Conflict);
    }

    [Fact]
    public void Is_an_operation_plan_and_dry_run_flows()
    {
        OperationPlan plan = WritePlan.Move(File, Folder) with { DryRun = true };
        Assert.True(plan.DryRun);
        Assert.True(typeof(OperationPlan).IsAssignableFrom(typeof(WritePlan)));
    }
}

public sealed class ErrorFacts
{
    public static TheoryData<StorageException, string> Errors() => new()
    {
        { new NotFound(new ItemId("i")), "not-found" },
        { new Conflict(new ItemId("i")), "conflict" },
        { new Denied(new ItemId("i")), "denied" },
        { new QuotaExceeded(null), "quota-exceeded" },
        { new Unavailable(null), "unavailable" },
        { new NeedsAuth(null), "needs-auth" },
        { new Unsupported(null), "unsupported" },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Each_error_has_a_stable_code(StorageException error, string code)
    {
        Assert.Equal(code, error.Code);
        Assert.Contains(code, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Item_flows_through()
    {
        Assert.Equal(new ItemId("i"), new NotFound(new ItemId("i")).Item);
        Assert.Null(new Unavailable(null).Item);
        Assert.True(typeof(StorageException).IsAbstract);
    }
}

public sealed class ContractShapeFacts
{
    [Fact]
    public void Storage_is_storage_v1()
    {
        var attribute = typeof(IStorage).GetCustomAttribute<CapabilityAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(Capability.Storage.V1, attribute.Id);
    }

    [Theory]
    [InlineData(typeof(OperationStarted), "storage.operation.started")]
    [InlineData(typeof(OperationProgress), "storage.operation.progress")]
    [InlineData(typeof(OperationDone), "storage.operation.done")]
    public void Operation_signals_carry_aliases(Type type, string alias)
    {
        Assert.Equal(alias, type.GetCustomAttribute<SignalAttribute>()!.Alias);
        Assert.True(typeof(Signal).IsAssignableFrom(type));
    }

    [Fact]
    public void Stream_members_have_the_phase_7_shapes()
    {
        var openRead = typeof(IStorage).GetMethod("OpenRead")!;
        Assert.Equal(typeof(Task<StorageReadStream>), openRead.ReturnType);
        Assert.Equal([typeof(ItemId), typeof(ByteRange?), typeof(CancellationToken)], openRead.GetParameters().Select(p => p.ParameterType));
        var openWrite = typeof(IStorage).GetMethod("OpenWrite")!;
        Assert.Equal(typeof(Task<StorageWriteStream>), openWrite.ReturnType);
        Assert.Equal([typeof(WritePlan), typeof(CancellationToken)], openWrite.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void Every_member_is_described()
    {
        Assert.All(typeof(IStorage).GetMethods(), method =>
            Assert.NotNull(method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()));
    }

    [Fact]
    public void Stream_handles_are_async_disposable()
    {
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(StorageReadStream)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(StorageWriteStream)));
        Assert.Equal(typeof(Stream), typeof(StorageWriteStream).GetProperty("Content")!.PropertyType);
        Assert.Equal(typeof(Task<ItemId>), typeof(StorageWriteStream).GetMethod("Commit")!.ReturnType);
        Assert.Equal(typeof(Task), typeof(StorageWriteStream).GetMethod("Abort")!.ReturnType);
    }

    [Fact]
    public async Task Disposing_an_uncommitted_write_aborts()
    {
        var write = new RecordingWrite();
        await write.DisposeAsync();
        Assert.Equal(1, write.Aborts);

        var committed = new RecordingWrite();
        Assert.Equal(new ItemId("new"), await committed.Commit(TestContext.Current.CancellationToken));
        await committed.DisposeAsync();
        Assert.Equal(0, committed.Aborts);
    }

    [Fact]
    public async Task Read_text_decodes_utf8()
    {
        var text = await new BytesStorage("héllo"u8.ToArray()).ReadText(new ItemId("a"), TestContext.Current.CancellationToken);
        Assert.Equal("héllo", text);
    }

    private sealed class RecordingWrite() : StorageWriteStream(new MemoryStream())
    {
        public int Aborts { get; private set; }

        protected override Task<ItemId> CommitCore(CancellationToken ct) => Task.FromResult(new ItemId("new"));

        protected override Task AbortCore(CancellationToken ct)
        {
            Aborts++;
            return Task.CompletedTask;
        }
    }

    private sealed class BytesStorage(byte[] bytes) : IStorage
    {
        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default) =>
            Task.FromResult(new Content(bytes, "text/plain", bytes.Length));

        public Task<StorageDescriptor> Describe(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Item> Stat(ItemId id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // A range crosses a runtime call as JSON: the struct must bind through its constructor, not the default one (05-08).
    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public void AByteRangeRoundTripsThroughJson()
    {
        var json = System.Text.Json.JsonSerializer.Serialize<ByteRange?>(new ByteRange(2, 10), Web);
        var back = System.Text.Json.JsonSerializer.Deserialize<ByteRange?>(json, Web);
        Assert.Equal((2L, (long?)10), (back!.Value.Offset, back.Value.Length));
    }
}
