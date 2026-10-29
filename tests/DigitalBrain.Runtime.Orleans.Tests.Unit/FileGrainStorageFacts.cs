using System.Text.Json;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class FileGrainStorageFacts : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "db-fgs-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); } }

    private FileGrainStorage Store() => new("platform", _root, "svc", new JsonSerializerForTests());

    private static GrainId Grain(string key = "k1") => GrainId.Create("probe", key);

    [Fact]
    public async Task Write_then_read_on_a_new_instance_returns_state_etag_and_record_exists()
    {
        var written = new GrainState<Counter> { State = new Counter { Value = 7 } };
        await Store().WriteStateAsync("s", Grain(), written);

        var read = new GrainState<Counter>();
        await Store().ReadStateAsync("s", Grain(), read);

        Assert.Equal(7, read.State!.Value);
        Assert.Equal(written.ETag, read.ETag);
        Assert.True(read.RecordExists);
    }

    [Fact]
    public async Task A_missing_record_reads_as_default_state_without_record()
    {
        var read = new GrainState<Counter>();
        await Store().ReadStateAsync("s", Grain(), read);
        Assert.False(read.RecordExists);
        Assert.Null(read.ETag);
        Assert.Equal(0, read.State!.Value);
    }

    [Fact]
    public async Task A_stale_etag_write_is_inconsistent()
    {
        var store = Store();
        var first = new GrainState<Counter> { State = new Counter { Value = 1 } };
        await store.WriteStateAsync("s", Grain(), first);
        var stale = new GrainState<Counter> { State = new Counter { Value = 2 }, ETag = "old" };
        await Assert.ThrowsAsync<InconsistentStateException>(() => store.WriteStateAsync("s", Grain(), stale));
    }

    [Fact]
    public async Task A_write_with_an_etag_when_no_record_exists_is_inconsistent()
    {
        var state = new GrainState<Counter> { State = new Counter(), ETag = "x" };
        await Assert.ThrowsAsync<InconsistentStateException>(() => Store().WriteStateAsync("s", Grain(), state));
    }

    [Fact]
    public async Task Clear_removes_the_record()
    {
        var store = Store();
        var state = new GrainState<Counter> { State = new Counter { Value = 3 } };
        await store.WriteStateAsync("s", Grain(), state);
        await store.ClearStateAsync("s", Grain(), state);
        var read = new GrainState<Counter>();
        await Store().ReadStateAsync("s", Grain(), read);
        Assert.False(read.RecordExists);
        Assert.Empty(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public async Task Two_concurrent_writers_with_the_same_etag_exactly_one_succeeds()
    {
        var store = Store();
        var seed = new GrainState<Counter> { State = new Counter() };
        await store.WriteStateAsync("s", Grain(), seed);
        var a = new GrainState<Counter> { State = new Counter { Value = 1 }, ETag = seed.ETag };
        var b = new GrainState<Counter> { State = new Counter { Value = 2 }, ETag = seed.ETag };

        var results = await Task.WhenAll(Attempt(store, a), Attempt(store, b));

        Assert.Equal(1, results.Count(ok => ok));
    }

    [Fact]
    public async Task A_leftover_temp_file_is_ignored()
    {
        Directory.CreateDirectory(_root);
        var store = Store();
        await File.WriteAllTextAsync(store.PathOf("s", Grain()) + ".dead.tmp", "{ torn", TestContext.Current.CancellationToken);
        var read = new GrainState<Counter>();
        await store.ReadStateAsync("s", Grain(), read);
        Assert.False(read.RecordExists);
        await store.WriteStateAsync("s", Grain(), new GrainState<Counter> { State = new Counter { Value = 5 } });
        await store.ReadStateAsync("s", Grain(), read);
        Assert.Equal(5, read.State!.Value);
    }

    [Theory]
    [InlineData("../../escape")]
    [InlineData("a/b:c")]
    [InlineData("..")]
    public async Task File_names_are_sha256_hex_so_keys_never_form_a_path(string key)
    {
        var store = Store();
        await store.WriteStateAsync("s", Grain(key), new GrainState<Counter> { State = new Counter { Value = 1 } });
        var file = Assert.Single(Directory.GetFiles(_root));
        Assert.Equal(_root, Path.GetDirectoryName(file));
        Assert.Matches("^[0-9a-f]{64}\\.json$", Path.GetFileName(file));
    }

    [Fact]
    public void File_names_are_length_prefixed_so_parts_cannot_be_shifted()
        => Assert.NotEqual(FileGrainStorage.FileNameOf("a", "b|c", "d", "e"), FileGrainStorage.FileNameOf("a", "b", "c|d", "e"));

    private static async Task<bool> Attempt(FileGrainStorage store, GrainState<Counter> state)
    {
        try { await store.WriteStateAsync("s", Grain(), state); return true; }
        catch (InconsistentStateException) { return false; }
    }

    public sealed class Counter
    {
        public int Value { get; set; }
    }
}

internal sealed class JsonSerializerForTests : IGrainStorageSerializer
{
    public BinaryData Serialize<T>(T? input) => new(JsonSerializer.SerializeToUtf8Bytes(input));

    public T Deserialize<T>(BinaryData input) => JsonSerializer.Deserialize<T>(input.ToArray())!;
}
