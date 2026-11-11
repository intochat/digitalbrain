using DigitalBrain.Kernel;
using DigitalBrain.Signals;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class DeliveryFacts
{
    private sealed record TestSignal(int Number) : Signal;

    private sealed class FakeJournal(BrainId brain) : IBrainJournal
    {
        public BrainId Brain => brain;
        public List<Commit> Commits { get; } = [];

        public Task<CommitResult> Append(Commit commit, CancellationToken ct = default)
        {
            Commits.Add(commit);
            return Task.FromResult(new CommitResult(Commits.Count, 1, new Position(Commits.Count), DateTimeOffset.UtcNow));
        }

        public Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
            => Task.FromResult<JournalRecord?>(null);

        public Task<SignalPage> Read(long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeJournalView : IJournalView
    {
        public Position Last { get; set; } = new(0);
        public SubscriptionIndex Subscriptions { get; } = new();
        public List<PositionedSignal> AvailableSignals { get; } = [];
        public Position Floor { get; set; } = new(0);

        public Position FloorOf(NeuronId? publisher) => Floor;
        public Position LastOf(NeuronId publisher) => Last;
        public IEnumerable<AliasMark> Marks(NeuronId? publisher) => [];
        public long SequenceAt(NeuronId publisher, Position position) => position.Value;

        public Task<JournalPage> ReadAfter(Position after, NeuronId? publisher, int limit, CancellationToken ct)
        {
            var matches = AvailableSignals
                .Where(s => s.Position.Value > after.Value && (publisher is null || s.Journaled.Publisher == publisher))
                .Take(limit)
                .ToList();
            return Task.FromResult(new JournalPage(Floor, Last, matches));
        }
    }

    private sealed class FakeConsumer : ISignalConsumer
    {
        public bool TakesUntyped => false;
        public List<JournaledSignal> Consumed { get; } = [];
        public bool ThrowOnConsume { get; set; }

        public Task Consume(JournaledSignal signal, Func<IDisposable> enter, CancellationToken ct)
        {
            using var scope = enter();
            if (ThrowOnConsume)
            {
                throw new InvalidOperationException("Reaction failed");
            }
            Consumed.Add(signal);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Nudge_with_cursor_at_or_past_upTo_is_dropped_as_duplicate()
    {
        var sub = NeuronId.Parse("b1/sub/1");
        var pub = NeuronId.Parse("b1/pub/1");
        var journal = new FakeJournal(sub.Brain);
        var view = new FakeJournalView();
        var consumer = new FakeConsumer();
        var counters = new DeliveryCounters();

        var delivery = new Delivery(sub, journal, view, consumer, _ => typeof(TestSignal), TimeProvider.System, counters);
        await delivery.Initialize(TestContext.Current.CancellationToken);
        await delivery.Ensure(pub, [SignalAliases.Of(typeof(TestSignal))], DateTimeOffset.UtcNow);

        // Advance cursor manually or via CatchUp
        Assert.Equal(0, counters.DuplicatesDropped);

        // Nudge with position 0 (which is <= cursor 0)
        await delivery.Nudged(new Position(0));
        Assert.Equal(1, counters.DuplicatesDropped);
    }

    [Fact]
    public async Task CatchUp_delivers_signals_and_flushes_cursor()
    {
        var sub = NeuronId.Parse("b1/sub/1");
        var pub = NeuronId.Parse("b1/pub/1");
        var journal = new FakeJournal(sub.Brain);
        var view = new FakeJournalView();
        var consumer = new FakeConsumer();
        var counters = new DeliveryCounters();

        var alias = SignalAliases.Of(typeof(TestSignal));
        var delivery = new Delivery(sub, journal, view, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize(TestContext.Current.CancellationToken);
        await delivery.Ensure(pub, [alias], DateTimeOffset.UtcNow);

        var sig1 = new JournaledSignal(pub, 1, new TestSignal(42), DateTimeOffset.UtcNow);
        view.AvailableSignals.Add(new PositionedSignal(new Position(1), alias, sig1));
        view.Last = new Position(1);

        await delivery.CatchUp(nudged: true);

        Assert.Single(consumer.Consumed);
        Assert.Equal(42, ((TestSignal)consumer.Consumed[0].Signal).Number);
        Assert.Equal(new Position(1), delivery.Subscriptions.Of(pub)!.Cursor);
    }

    [Fact]
    public async Task Failed_reaction_retreats_cursor_and_increments_failure_counter()
    {
        var sub = NeuronId.Parse("b1/sub/1");
        var pub = NeuronId.Parse("b1/pub/1");
        var journal = new FakeJournal(sub.Brain);
        var view = new FakeJournalView();
        var consumer = new FakeConsumer { ThrowOnConsume = true };
        var counters = new DeliveryCounters();

        var alias = SignalAliases.Of(typeof(TestSignal));
        var delivery = new Delivery(sub, journal, view, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize(TestContext.Current.CancellationToken);
        await delivery.Ensure(pub, [alias], DateTimeOffset.UtcNow);

        var initialCursor = delivery.Subscriptions.Of(pub)!.Cursor;

        var sig1 = new JournaledSignal(pub, 1, new TestSignal(42), DateTimeOffset.UtcNow);
        view.AvailableSignals.Add(new PositionedSignal(new Position(1), alias, sig1));
        view.Last = new Position(1);

        await delivery.CatchUp(nudged: true);

        Assert.Equal(1, counters.ReactionFailures);
        Assert.Equal(initialCursor, delivery.Subscriptions.Of(pub)!.Cursor);
    }
}
