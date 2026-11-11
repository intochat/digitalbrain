using DigitalBrain.Kernel;
using DigitalBrain.Signals;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

internal interface IJournalTarget : IAsyncDisposable
{
    IBrainJournal Journal { get; }
    IJournalView View { get; }
    Task Reopen();
}

public abstract class JournalConformance
{
    protected static readonly BrainId Brain = new("b1");
    protected static readonly NeuronId P = NeuronId.Parse("b1/chat/p");
    protected static readonly NeuronId Q = NeuronId.Parse("b1/chat/q");

    internal abstract Task<IJournalTarget> CreateTargetAsync();

    [Fact]
    public async Task Empty_commit_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var empty = new Commit(P, null, [], null, new Stamp(null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => target.Journal.Append(empty, ct));
    }

    [Fact]
    public async Task State_append_advances_sequence_and_version()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var bytes = "test data"u8.ToArray();
        var commit = new Commit(P, new StateWrite("state", StateChange.Write, bytes, null), [], null, new Stamp(null, null));
        var result = await target.Journal.Append(commit, ct);

        Assert.Equal(1L, result.Version);
        var record = await target.Journal.ReadRecord(P, "state", ct);
        Assert.NotNull(record);
        Assert.Equal(1L, record.Version);
        Assert.Equal(bytes, record.Bytes.ToArray());
    }

    [Fact]
    public async Task State_read_returns_written_bytes_and_version()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var record = await target.Journal.ReadRecord(P, "unknown", ct);
        Assert.Null(record);

        var bytes = "state bytes"u8.ToArray();
        await target.Journal.Append(new Commit(P, new StateWrite("data", StateChange.Write, bytes, null), [], null, new Stamp(null, null)), ct);
        record = await target.Journal.ReadRecord(P, "data", ct);
        Assert.NotNull(record);
        Assert.Equal(bytes, record.Bytes.ToArray());
        Assert.Equal(1L, record.Version);
    }

    [Fact]
    public async Task Signal_append_assigns_publisher_sequence_and_brain_position()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var commit = new Commit(P, null, [new Note("sig1")], null, new Stamp(null, null));
        var result = await target.Journal.Append(commit, ct);

        Assert.Equal(1L, result.FirstSequence);
        Assert.Equal(1L, result.FirstPosition.Value);
        Assert.Equal(1L, target.View.Last.Value);
    }

    [Fact]
    public async Task Multi_signal_append_assigns_contiguous_sequences_and_positions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var c1 = new Commit(P, null, [new Note("a"), new Note("b"), new Note("c")], null, new Stamp(null, null));
        var r1 = await target.Journal.Append(c1, ct);
        Assert.Equal(1L, r1.FirstSequence);
        Assert.Equal(1L, r1.FirstPosition.Value);
        Assert.Equal(3L, target.View.Last.Value);

        var c2 = new Commit(P, null, [new Note("d"), new Note("e")], null, new Stamp(null, null));
        var r2 = await target.Journal.Append(c2, ct);
        Assert.Equal(4L, r2.FirstSequence);
        Assert.Equal(4L, r2.FirstPosition.Value);
        Assert.Equal(5L, target.View.Last.Value);
    }

    [Fact]
    public async Task Stale_state_version_refuses_the_whole_commit_and_throws_StaleVersionException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var commit = new Commit(P, new StateWrite("state", StateChange.Write, "data"u8.ToArray(), Expected: 99), [new Note("sig")], null, new Stamp(null, null));
        await Assert.ThrowsAsync<StaleVersionException>(() => target.Journal.Append(commit, ct));
    }

    [Fact]
    public async Task Stale_cursor_version_refuses_the_whole_commit_and_throws_StaleVersionException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var commit = new Commit(P, null, [new Note("sig")], new CursorMove("cursor", "move"u8.ToArray(), Expected: 99), new Stamp(null, null));
        await Assert.ThrowsAsync<StaleVersionException>(() => target.Journal.Append(commit, ct));
    }

    [Fact]
    public async Task Refused_commit_consumes_no_sequence_and_no_position()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var r1 = await target.Journal.Append(new Commit(P, new StateWrite("state", StateChange.Write, "first"u8.ToArray(), null), [], null, new Stamp(null, null)), ct);
        Assert.Equal(1L, r1.Version);

        await Assert.ThrowsAsync<StaleVersionException>(() =>
            target.Journal.Append(new Commit(P, new StateWrite("state", StateChange.Write, "bad"u8.ToArray(), Expected: 99), [new Note("s")], null, new Stamp(null, null)), ct));

        var r2 = await target.Journal.Append(new Commit(P, new StateWrite("state", StateChange.Write, "second"u8.ToArray(), Expected: 1L), [], null, new Stamp(null, null)), ct);
        Assert.Equal(2L, r2.Version);
    }

    [Fact]
    public async Task State_signals_and_cursor_commit_atomically_together()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var stateBytes = "state data"u8.ToArray();
        var cursorBytes = Subscriptions.None(P).Encode();
        var commit = new Commit(P,
            new StateWrite("state", StateChange.Write, stateBytes, null),
            [new Note("hello")],
            new CursorMove("cursor", cursorBytes, null),
            new Stamp("trace-1", "cause-1"));

        var result = await target.Journal.Append(commit, ct);
        Assert.Equal(1L, result.Version);
        Assert.Equal(1L, result.FirstSequence);
        Assert.Equal(1L, result.FirstPosition.Value);

        var state = await target.Journal.ReadRecord(P, "state", ct);
        Assert.NotNull(state);
        Assert.Equal(1L, state.Version);

        var cursor = await target.Journal.ReadRecord(P, "cursor", ct);
        Assert.NotNull(cursor);
        Assert.Equal(1L, cursor.Version);

        var page = await target.View.ReadAfter(new Position(0), P, 10, ct);
        Assert.Single(page.Signals);
        Assert.Equal("trace-1", page.Signals[0].Journaled.TraceId);
    }

    [Fact]
    public async Task Multiple_publishers_receive_independent_sequences_and_ordered_brain_positions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var pResult = await target.Journal.Append(new Commit(P, null, [new Note("p1"), new Note("p2")], null, new Stamp(null, null)), ct);
        Assert.Equal(1L, pResult.FirstSequence);
        Assert.Equal(1L, pResult.FirstPosition.Value);

        var qResult = await target.Journal.Append(new Commit(Q, null, [new Note("q1"), new Note("q2")], null, new Stamp(null, null)), ct);
        Assert.Equal(1L, qResult.FirstSequence);
        Assert.Equal(3L, qResult.FirstPosition.Value);

        Assert.Equal(4L, target.View.Last.Value);
        Assert.Equal(2L, target.View.LastOf(P).Value);
        Assert.Equal(4L, target.View.LastOf(Q).Value);
    }

    [Fact]
    public async Task Duplicate_state_writes_with_correct_expected_version_succeed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var r1 = await target.Journal.Append(new Commit(P, new StateWrite("counter", StateChange.Write, new byte[] { 1 }, null), [], null, new Stamp(null, null)), ct);
        Assert.Equal(1L, r1.Version);

        var r2 = await target.Journal.Append(new Commit(P, new StateWrite("counter", StateChange.Write, new byte[] { 2 }, Expected: 1L), [], null, new Stamp(null, null)), ct);
        Assert.Equal(2L, r2.Version);

        var r3 = await target.Journal.Append(new Commit(P, new StateWrite("counter", StateChange.Write, new byte[] { 3 }, Expected: 2L), [], null, new Stamp(null, null)), ct);
        Assert.Equal(3L, r3.Version);

        var record = await target.Journal.ReadRecord(P, "counter", ct);
        Assert.NotNull(record);
        Assert.Equal(3L, record.Version);
        Assert.Equal(new byte[] { 3 }, record.Bytes.ToArray());
    }

    [Fact]
    public async Task Clear_state_removes_record_and_increments_version()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        await target.Journal.Append(new Commit(P, new StateWrite("temp", StateChange.Write, new byte[] { 42 }, null), [], null, new Stamp(null, null)), ct);
        Assert.NotNull(await target.Journal.ReadRecord(P, "temp", ct));

        var r2 = await target.Journal.Append(new Commit(P, new StateWrite("temp", StateChange.Clear, ReadOnlyMemory<byte>.Empty, Expected: 1L), [], null, new Stamp(null, null)), ct);
        Assert.Equal(2L, r2.Version);
        Assert.Null(await target.Journal.ReadRecord(P, "temp", ct));
    }

    [Fact]
    public async Task ReadSignals_after_position_returns_exact_signals_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        await target.Journal.Append(new Commit(P, null, [new Note("one"), new Note("two"), new Note("three")], null, new Stamp(null, null)), ct);

        var pAll = await target.View.ReadAfter(new Position(0), P, 10, ct);
        Assert.Equal(3, pAll.Signals.Count);
        Assert.Equal(new Position(1), pAll.Signals[0].Position);
        Assert.Equal(new Position(2), pAll.Signals[1].Position);
        Assert.Equal(new Position(3), pAll.Signals[2].Position);

        var pAfter2 = await target.View.ReadAfter(new Position(2), P, 10, ct);
        Assert.Single(pAfter2.Signals);
        Assert.Equal(new Position(3), pAfter2.Signals[0].Position);
    }

    [Fact]
    public async Task Reopen_preserves_records_versions_sequences_and_positions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var bytes = "persisted"u8.ToArray();
        await target.Journal.Append(new Commit(P, new StateWrite("perm", StateChange.Write, bytes, null), [new Note("durable")], null, new Stamp(null, null)), ct);

        await target.Reopen();

        var record = await target.Journal.ReadRecord(P, "perm", ct);
        Assert.NotNull(record);
        Assert.Equal(bytes, record.Bytes.ToArray());
        Assert.Equal(1L, record.Version);

        Assert.Equal(1L, target.View.Last.Value);
        var page = await target.View.ReadAfter(new Position(0), P, 10, ct);
        Assert.Single(page.Signals);
    }

    [Fact]
    public void Non_contiguous_journal_replay_throws_InvalidDataException()
    {
        var journal = new Journal<string>();
        var entry1 = new JournalEntry<string>(1, "o", null, null, "p", ["s1"], 1, 1, 100);
        journal.Apply(entry1);

        // Gap in sequence: sequence 3 instead of 2
        var entryGap = new JournalEntry<string>(3, "o", null, null, "p", ["s2"], 2, 2, 200);
        Assert.Throws<InvalidDataException>(() => journal.Apply(entryGap));
    }

    [Fact]
    public async Task Gap_by_position_with_two_publishers_reported_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        await target.Journal.Append(new Commit(P, null, [new Note("p1"), new Note("p2")], null, new Stamp(null, null)), ct);
        await target.Journal.Append(new Commit(Q, null, [new Note("q1"), new Note("q2")], null, new Stamp(null, null)), ct);

        Assert.Equal(new Position(2), target.View.LastOf(P));
        Assert.Equal(new Position(4), target.View.LastOf(Q));
    }

    [Fact]
    public async Task Brain_wide_floor_tracks_highest_trimmed_publisher()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        await target.Journal.Append(new Commit(P, null, [new Note("p1")], null, new Stamp(null, null)), ct);
        Assert.Equal(new Position(0), target.View.FloorOf(null));
        Assert.Equal(new Position(0), target.View.FloorOf(P));
    }

    [Fact]
    public async Task Subscription_index_folded_correctly_from_records_after_replay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var subs = Subscriptions.None(P).Add(Q, ["test.note"], new Position(0), DateTimeOffset.UtcNow);
        var bytes = subs.Encode();
        await target.Journal.Append(new Commit(P, new StateWrite(Subscriptions.RecordName, StateChange.Write, bytes, null), [], null, new Stamp(null, null)), ct);

        Assert.Contains(target.View.Subscriptions.All, s => s.Subscriber == P);
        Assert.Contains(P, target.View.Subscriptions.Hearing(Q, ["test.note"], _ => typeof(Note)));
    }

    [Fact]
    public async Task Subscription_index_rebuilt_identically_after_restore()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var target = await CreateTargetAsync();
        var subs = Subscriptions.None(P).Add(Q, ["test.note"], new Position(5), DateTimeOffset.UtcNow);
        var bytes = subs.Encode();
        await target.Journal.Append(new Commit(P, new StateWrite(Subscriptions.RecordName, StateChange.Write, bytes, null), [], null, new Stamp(null, null)), ct);

        await target.Reopen();

        Assert.Contains(target.View.Subscriptions.All, s => s.Subscriber == P);
        var reloaded = target.View.Subscriptions.All.First(s => s.Subscriber == P);
        Assert.Equal(new Position(5), reloaded.Of(Q)?.Cursor);
    }
}
