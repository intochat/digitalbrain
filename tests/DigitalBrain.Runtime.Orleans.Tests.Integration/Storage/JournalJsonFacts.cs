using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

public sealed class JournalJsonFacts
{
    private static readonly string PGrain = BrainStore.GrainOf(BrainStore.P).ToString();

    [Fact]
    public async Task Plain_record_signal_and_state_round_trip_through_append_checkpoint_restart_and_grain_state()
    {
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var grain = BrainStore.GrainOf(BrainStore.P);
        var state = new TestGrainState<Memo> { State = new Memo("first", 1, ["a"]) };
        await store.Storage.WriteStateAsync("state", grain, state, BrainStore.P.ToString(), [new Note("one")]);
        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);
        state.State = new Memo("second", 2, ["a", "b"]);
        await store.Storage.WriteStateAsync("state", grain, state, BrainStore.P.ToString(), [new Note("two")]);

        await store.Reopen();

        var read = new TestGrainState<Memo>();
        await store.Storage.ReadStateAsync("state", grain, read);
        Assert.True(read.RecordExists);
        Assert.Equal("second", read.State.Title);
        Assert.Equal(["a", "b"], read.State.Tags);
        Assert.Equal(state.ETag, read.ETag);
        var page = await store.History().Read(BrainStore.P, 0, 10, TestContext.Current.CancellationToken);
        Assert.Equal([new Note("one"), new Note("two")], page.Signals.Select(s => s.Signal));
        Assert.Equal([1L, 2L], page.Signals.Select(s => s.Sequence));
    }

    [Fact]
    public async Task An_entry_with_a_side_state_writes_both_records_at_one_sequence_or_neither()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var log = await store.Log();
        var cursors = """{"Sources":[{"Source":"b1/chat/q","Aliases":["test.note"],"Cursor":3}]}"""u8.ToArray();
        var (first, _) = await log.AppendEntry(PGrain, "state", null, BrainRecordChange.Write, JournalPayloads.EncodeState(new Memo("m", 1, [])),
            BrainStore.P.ToString(), [new Note("reacted")], new BrainSideWrite("synapses", null, cursors), ct);

        // A stale side ETag refuses the whole entry: neither the state nor the cursors move.
        await Assert.ThrowsAsync<InconsistentStateException>(() => log.AppendEntry(PGrain, "state", first, BrainRecordChange.Write,
            JournalPayloads.EncodeState(new Memo("m", 2, [])), BrainStore.P.ToString(), [new Note("again")], new BrainSideWrite("synapses", null, cursors), ct));

        await store.Reopen();
        log = await store.Log();
        Assert.Equal(first, (await log.Read(PGrain, "state"))!.ETag);
        var side = (await log.Read(PGrain, "synapses"))!;
        Assert.Equal(first, side.ETag);
        Assert.Equal(cursors, side.Record);
        Assert.Single((await log.PublisherSignals(BrainStore.P.ToString(), 0, 10, ct)).Held);
    }

    [Fact]
    public async Task An_entry_with_two_signal_types_stores_two_json_payloads_named_by_alias()
    {
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var log = await store.Log();
        await log.AppendEntry(PGrain, "state", null, BrainRecordChange.Write, JournalPayloads.EncodeState(new Memo("m", 0, [])),
            BrainStore.P.ToString(), [new Note("hi"), new Count(7)], TestContext.Current.CancellationToken);

        var slice = await log.PublisherSignals(BrainStore.P.ToString(), 0, 10, TestContext.Current.CancellationToken);
        Assert.Equal(["test.note", "test.count"], slice.Held.Select(s => s.Payload.Type));
        Assert.Equal("hi", JsonDocument.Parse(slice.Held[0].Payload.Json).RootElement.GetProperty("text").GetString());
        Assert.Equal(7, JsonDocument.Parse(slice.Held[1].Payload.Json).RootElement.GetProperty("n").GetInt32());

        await store.Reopen();
        var page = await store.History().Read(BrainStore.P, 0, 10, TestContext.Current.CancellationToken);
        Assert.Equal([new Note("hi"), new Count(7)], page.Signals.Select(s => s.Signal));
    }

    [Fact]
    public async Task An_unregistered_alias_or_json_deeper_than_128_throws_JournalPayloadException()
    {
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var payloads = store.Payloads;
        Assert.Throws<JournalPayloadException>(() => payloads.DecodeSignal(new JournaledSignalPayload("test.unknown", "{}"u8.ToArray())));
        var deep = new StringBuilder();
        for (var i = 0; i < 200; i++) { deep.Append("{\"x\":"); }
        deep.Append('1');
        deep.Append('}', 200);
        Assert.Throws<JournalPayloadException>(() => payloads.DecodeSignal(new JournaledSignalPayload("test.note", Encoding.UTF8.GetBytes(deep.ToString()))));
        Assert.Throws<JournalPayloadException>(() => JournalPayloads.DecodeState<Memo>(Encoding.UTF8.GetBytes(deep.ToString())));
    }

    [Theory]
    [InlineData("Framed")]
    [InlineData("Legacy")]
    public async Task A_journal_in_the_previous_format_is_refused_naming_SAFE_01(string found)
    {
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        await store.Log(); // creates the brain key and an empty journal
        var directory = store.DirectoryOf();
        await store.Reopen();
        // An os journal: "DBLG" + version 1 (Framed), or a bare length prefix (Legacy), then a sealed frame.
        var frame = new byte[4 + 40];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 40);
        byte[] old = found == "Framed" ? [.. "DBLG"u8, 1, .. frame] : frame;
        await File.WriteAllBytesAsync(Path.Combine(directory, "initial.log"), old, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<BrainLogFormatException>(() => store.Log());
        Assert.Contains("SAFE-01", error.Message, StringComparison.Ordinal);
        Assert.Contains(found, error.Message, StringComparison.Ordinal);
        // Nothing is recovered partially: the file is untouched and the next open refuses again.
        Assert.Equal(old, await File.ReadAllBytesAsync(Path.Combine(directory, "initial.log"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<BrainLogFormatException>(() => store.Log());
    }

    [Fact]
    public async Task Every_entry_carries_AtUnixMs_from_the_injected_TimeProvider()
    {
        using var home = new TempHome();
        var time = new ManualTime();
        await using var store = new BrainStore(home.Path, services => services.AddSingleton<TimeProvider>(time));
        await store.Publish(BrainStore.P, 2, batch: 1, beforeEntry: n => time.Now = time.Now.AddMinutes(n));

        var slice = await (await store.Log()).PublisherSignals(BrainStore.P.ToString(), 0, 10, TestContext.Current.CancellationToken);
        var start = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal([start.ToUnixTimeMilliseconds(), start.AddMinutes(1).ToUnixTimeMilliseconds()], slice.Held.Select(s => s.AtUnixMs));
        await store.Reopen();
        var page = await store.History().Read(BrainStore.P, 0, 10, TestContext.Current.CancellationToken);
        Assert.Equal([start, start.AddMinutes(1)], page.Signals.Select(s => s.At));
    }

    [Fact]
    public async Task AddBrainStorage_replaces_only_the_cluster_store_so_PubSubStore_and_reminders_keep_their_Orleans_encoding()
    {
        using var home = new TempHome();
        var pubsub = new FakeStorage();
        await using var store = new BrainStore(home.Path, services =>
        {
            services.AddKeyedSingleton<IGrainStorage>(IntoChatOSNames.PubSubStore, pubsub);
            services.AddKeyedSingleton<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage, new FakeStorage());
        });
        Assert.Same(pubsub, store.Services.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.PubSubStore));
        Assert.IsType<BrainGrainStorage>(store.Services.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage));
        Assert.Single(store.Services.GetKeyedServices<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage));
    }

    private sealed class FakeStorage : IGrainStorage
    {
        public Task ReadStateAsync<T>(string stateName, OrleansSdk.Runtime.GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;

        public Task WriteStateAsync<T>(string stateName, OrleansSdk.Runtime.GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;

        public Task ClearStateAsync<T>(string stateName, OrleansSdk.Runtime.GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;
    }
}
