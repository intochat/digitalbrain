using DigitalBrain.Kernel;
using DigitalBrain.Signals;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class KernelStateFacts
{
    private sealed record TestPayload(string Text);
    private sealed record TestSignal(string Message) : Signal;

    [EphemeralSignal]
    private sealed record TestEphemeral(string Note) : Signal;

    private sealed class FakeJournal(BrainId brain) : IBrainJournal
    {
        public BrainId Brain => brain;
        public Commit? LastCommit { get; private set; }
        public JournalRecord? NextRecord { get; set; }
        public bool ThrowStale { get; set; }
        public long NextVersion { get; set; } = 1;

        public Task<CommitResult> Append(Commit commit, CancellationToken ct = default)
        {
            LastCommit = commit;
            if (ThrowStale)
            {
                throw new StaleVersionException(commit.Publisher.ToString(), "state", 0, commit.State?.Expected);
            }
            return Task.FromResult(new CommitResult(NextVersion++, 1, new Position(1), DateTimeOffset.UtcNow));
        }

        public Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
            => Task.FromResult(NextRecord);

        public Task<SignalPage> Read(long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeNudge : ISignalNudge
    {
        public List<JournaledSignal> AppendedSignals { get; } = [];
        public List<Signal> OfferedSignals { get; } = [];

        public Task Appended(NeuronId publisher, IReadOnlyList<JournaledSignal> appended, Position last, CancellationToken ct = default)
        {
            AppendedSignals.AddRange(appended);
            return Task.CompletedTask;
        }

        public void Appended(NeuronId publisher, IReadOnlyCollection<string> aliases, Position last) { }

        public Task Offered(NeuronId publisher, Signal signal, CancellationToken ct = default)
        {
            OfferedSignals.Add(signal);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Stateless_neuron_refuses_state_mutations()
    {
        var id = NeuronId.Parse("b1/stateless/1");
        var journal = new FakeJournal(id.Brain);
        var nudge = new FakeNudge();
        var dropped = false;

        var state = new KernelState(id, "state", stateless: true, journal, nudge, () => new Stamp(null, null), () => dropped = true);

        var loaded = await state.Load<TestPayload>();
        Assert.Null(loaded);

        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Save(new TestPayload("x"), [new TestSignal("s")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Store(new TestPayload("x")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Clear());
        Assert.False(dropped);
    }

    [Fact]
    public async Task Save_and_Store_commit_entries_and_nudge_appended()
    {
        var id = NeuronId.Parse("b1/stateful/1");
        var journal = new FakeJournal(id.Brain);
        var nudge = new FakeNudge();
        var dropped = false;

        var state = new KernelState(id, "state", stateless: false, journal, nudge, () => new Stamp("trace1", "cause1"), () => dropped = true);

        // Save with signals
        await state.Save(new TestPayload("v1"), [new TestSignal("msg1")]);
        Assert.NotNull(journal.LastCommit);
        Assert.Equal(id, journal.LastCommit.Publisher);
        Assert.NotNull(journal.LastCommit.State);
        Assert.Equal(StateChange.Write, journal.LastCommit.State.Change);
        Assert.Single(journal.LastCommit.Signals);
        Assert.Single(nudge.AppendedSignals);
        Assert.Equal("trace1", journal.LastCommit.Stamp.TraceId);

        // Store without signals
        await state.Store(new TestPayload("v2"));
        Assert.NotNull(journal.LastCommit.State);
        Assert.Empty(journal.LastCommit.Signals);
        Assert.False(dropped);
    }

    [Fact]
    public async Task Publish_distinguishes_ephemeral_from_durable_signals()
    {
        var id = NeuronId.Parse("b1/publisher/1");
        var journal = new FakeJournal(id.Brain);
        var nudge = new FakeNudge();

        var state = new KernelState(id, "state", stateless: false, journal, nudge, () => new Stamp(null, null), () => { });

        await state.Publish(new TestEphemeral("ephemeral"));
        Assert.Single(nudge.OfferedSignals);
        Assert.Null(journal.LastCommit);

        await state.Publish(new TestSignal("durable"));
        Assert.NotNull(journal.LastCommit);
        Assert.Single(journal.LastCommit.Signals);
    }

    [Fact]
    public async Task StaleVersionException_triggers_drop_callback_and_rethrows()
    {
        var id = NeuronId.Parse("b1/stale/1");
        var journal = new FakeJournal(id.Brain) { ThrowStale = true };
        var nudge = new FakeNudge();
        var dropped = false;

        var state = new KernelState(id, "state", stateless: false, journal, nudge, () => new Stamp(null, null), () => dropped = true);

        await Assert.ThrowsAsync<StaleVersionException>(() => state.Store(new TestPayload("conflict")));
        Assert.True(dropped);
    }
}
