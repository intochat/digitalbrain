using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

// 04-17 (R4-10): the adapters' write path into the per-brain BrainLog.
public sealed class BrainJournalWriterFacts
{
    private static readonly byte[] Opaque = [0x00, 0x01, 0xFE, 0xFF, 0x80]; // not JSON: an Orleans-serialized legacy state
    private const string Grain = "legacy.chat/b1/c1";
    private const string Publisher = "b1/legacy.chat/c1";

    private static IBrainJournalWriter Writer(BrainStore store) => store.Services.GetRequiredService<IBrainJournalWriter>();

    private static JournalSignalWrite Os(string type, string json) => new(type, System.Text.Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task An_adapter_entry_with_an_opaque_record_and_signals_round_trips_and_survives_a_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var writer = Writer(store);

        var first = await writer.Append("b1", new JournalStateWrite(Grain, "state", JournalStateChange.Write, null, Opaque, "IntoChat.OS.Chat.ChatState"),
            Publisher, [Os("os.chat.posted", """{"text":"a"}"""), Os("test.note", """{"text":"b"}""")], ct);
        var second = await writer.Append("b1", new JournalStateWrite(Grain, "state", JournalStateChange.Write, first.Sequence, new byte[] { 0x02 }, "IntoChat.OS.Chat.ChatState"),
            Publisher, [], ct);

        Assert.Equal((1L, 2L, 2L), (first.Sequence, first.LastSignal, first.LastPosition));
        Assert.Equal((2L, 2L), (second.Sequence, second.LastSignal));
        await store.Reopen();
        writer = Writer(store);
        var state = await writer.ReadState("b1", Grain, "state", ct);
        Assert.NotNull(state);
        Assert.Equal([0x02], state.Record.ToArray());
        Assert.Equal((2L, "IntoChat.OS.Chat.ChatState"), (state.ETag, state.StateType));
        var page = await writer.ReadSince("b1", 0, 10, ct);
        Assert.Equal((0L, 2L), (page.Floor, page.Last));
        Assert.Equal(["os.chat.posted", "test.note"], page.Signals.Select(s => s.Type));
        Assert.Equal("""{"text":"a"}""", System.Text.Encoding.UTF8.GetString(page.Signals[0].Json.Span));
        Assert.All(page.Signals, s => Assert.Equal(Publisher, s.Publisher));
        Assert.Equal([1L, 2L], page.Signals.Select(s => s.Sequence));
        Assert.Equal([2L], (await writer.ReadSince("b1", Publisher, 1, 10, ct)).Signals.Select(s => s.Sequence));
    }

    [Fact]
    public async Task Clear_removes_the_record_and_a_stale_ETag_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var writer = Writer(store);
        var written = await writer.Append("b1", new JournalStateWrite(Grain, "state", JournalStateChange.Write, null, Opaque, null), Publisher, [], ct);

        await Assert.ThrowsAsync<InconsistentStateException>(() =>
            writer.Append("b1", new JournalStateWrite(Grain, "state", JournalStateChange.Write, null, Opaque, null), Publisher, [], ct));
        await writer.Append("b1", new JournalStateWrite(Grain, "state", JournalStateChange.Clear, written.Sequence, default, null), Publisher, [], ct);

        Assert.Null(await writer.ReadState("b1", Grain, "state", ct));
    }

    [Fact]
    public async Task Payloads_that_are_not_json_or_do_not_fit_their_registered_signal_are_refused_before_the_disk()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var writer = Writer(store);

        await Assert.ThrowsAsync<JournalPayloadException>(() => writer.Append("b1", null, Publisher, [Os("os.chat.posted", "{not json")], ct));
        await Assert.ThrowsAsync<JournalPayloadException>(() => writer.Append("b1", null, Publisher, [Os("test.count", """{"n":"x"}""")], ct));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.Append("b1", null, Publisher, [], ct));

        Assert.Equal(0, (await writer.ReadSince("b1", 0, 10, ct)).Last);
    }

    [Fact]
    public async Task The_writer_shares_the_runtime_log_of_the_brain_and_a_second_writer_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var writer = Writer(store);
        await writer.Claim("b1", ct);
        await store.Publish(BrainStore.P, 2); // the runtime's own write path on the same log
        await writer.Append("b1", null, Publisher, [Os("os.chat.posted", "{}")], ct);

        Assert.Equal(3, (await store.Log()).LastPosition);
        Assert.True(File.Exists(Path.Combine(store.DirectoryOf(), "writer.lock")));
        await using var other = new BrainStore(home.Path); // another process on the same home
        await Assert.ThrowsAsync<BrainNotOwnedException>(() => Writer(other).Claim("b1", ct));
        await Assert.ThrowsAsync<BrainNotOwnedException>(() => Writer(other).Append("b1", null, Publisher, [Os("os.chat.posted", "{}")], ct));
    }
}
