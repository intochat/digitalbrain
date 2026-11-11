#pragma warning disable CA2201
#pragma warning disable CA1822
#pragma warning disable CA1040
#pragma warning disable xUnit1051
using System.Collections.Concurrent;
using DigitalBrain.Kernel;
using DigitalBrain.Signals;
using DigitalBrain.Spi;

namespace DigitalBrain.Tests.Unit.Kernel;

/// <summary>
/// Empirical adversarial challenge suite for Milestone 3:
/// Stream Elimination & Delivery Loop Challenger (R05).
/// Stress-tests:
/// 1. Single-Flight Delivery Loop under concurrent nudges, re-entrancy prevention, exception safety.
/// 2. Retention Floor & Gap Handling: Gap translation, sequence vs position type mismatch, brain-wide gaps.
/// 3. Nudge & Fanout via SubscriptionIndex, [OneWay] dispatch, Reconcile sweeps.
/// </summary>
public sealed class DeliveryChallengerFacts
{
    private sealed record TestSignal(int Number) : Signal;
    private sealed record OtherSignal(string Text) : Signal;

    private sealed class MockJournal(BrainId brain) : IBrainJournal
    {
        public BrainId Brain => brain;
        public List<Commit> Commits { get; } = [];
        public TaskCompletionSource? FlushAwaited { get; set; }
        public TaskCompletionSource? FlushContinue { get; set; }

        public async Task<CommitResult> Append(Commit commit, CancellationToken ct = default)
        {
            Commits.Add(commit);
            if (FlushAwaited is not null)
            {
                FlushAwaited.TrySetResult();
                if (FlushContinue is not null)
                {
                    await FlushContinue.Task.ConfigureAwait(false);
                }
            }
            return new CommitResult(Commits.Count, 1, new Position(Commits.Count), DateTimeOffset.UtcNow);
        }

        public Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
            => Task.FromResult<JournalRecord?>(null);

        public Task<SignalPage> Read(long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class DynamicJournalView : IJournalView
    {
        private readonly Lock _lock = new();
        public Position Last { get; set; } = new(0);
        public SubscriptionIndex Subscriptions { get; } = new();
        public List<PositionedSignal> Signals { get; } = [];
        public Dictionary<NeuronId, Position> Floors { get; } = [];
        public Dictionary<NeuronId, Position> LastOfMap { get; } = [];
        public Dictionary<NeuronId, Dictionary<Position, long>> SequenceAtMap { get; } = [];

        public Position DefaultFloor { get; set; } = new(0);

        public Position FloorOf(NeuronId? publisher)
        {
            lock (_lock)
            {
                return publisher is { } p && Floors.TryGetValue(p, out var f) ? f : DefaultFloor;
            }
        }

        public Position LastOf(NeuronId publisher)
        {
            lock (_lock)
            {
                return LastOfMap.TryGetValue(publisher, out var l) ? l : Last;
            }
        }

        public IEnumerable<AliasMark> Marks(NeuronId? publisher) => [];

        public long SequenceAt(NeuronId publisher, Position position)
        {
            lock (_lock)
            {
                if (SequenceAtMap.TryGetValue(publisher, out var map) && map.TryGetValue(position, out var seq))
                {
                    return seq;
                }
                return position.Value;
            }
        }

        public Task<JournalPage> ReadAfter(Position after, NeuronId? publisher, int limit, CancellationToken ct)
        {
            lock (_lock)
            {
                var floor = FloorOf(publisher);
                var matches = Signals
                    .Where(s => s.Position.Value > after.Value && (publisher is null || s.Journaled.Publisher == publisher))
                    .Take(limit)
                    .ToList();
                return Task.FromResult(new JournalPage(floor, Last, matches));
            }
        }
    }

    private sealed class ConcurrentConsumer : ISignalConsumer
    {
        public bool TakesUntyped => false;
        public ConcurrentQueue<JournaledSignal> Consumed { get; } = new();
        public int ConcurrencyLevel;
        public int MaxObservedConcurrency;
        public int ReactionDelayMs;
        public bool ThrowFirstTime;
        private int _attempts;

        public async Task Consume(JournaledSignal signal, Func<IDisposable> enter, CancellationToken ct)
        {
            var cur = Interlocked.Increment(ref ConcurrencyLevel);
            // Track max concurrency observed inside Consume
            int initialMax;
            do
            {
                initialMax = MaxObservedConcurrency;
                if (cur <= initialMax) { break; }
            } while (Interlocked.CompareExchange(ref MaxObservedConcurrency, cur, initialMax) != initialMax);

            try
            {
                using var scope = enter();
                if (ThrowFirstTime && Interlocked.Increment(ref _attempts) == 1)
                {
                    throw new InvalidOperationException("Simulated transient reaction fault");
                }

                if (ReactionDelayMs > 0)
                {
                    await Task.Delay(ReactionDelayMs, ct).ConfigureAwait(false);
                }

                Consumed.Enqueue(signal);
            }
            finally
            {
                Interlocked.Decrement(ref ConcurrencyLevel);
            }
        }
    }

    // =========================================================================
    // 1. Single-Flight Delivery Loop Concurrency Stress Tests
    // =========================================================================

    [Fact]
    public async Task Massive_concurrent_nudges_never_violate_single_flight_pump()
    {
        var sub = NeuronId.Parse("b1/sub/concurrency");
        var pub = NeuronId.Parse("b1/pub/concurrency");
        var journal = new MockJournal(sub.Brain);
        var view = new DynamicJournalView();
        var consumer = new ConcurrentConsumer { ReactionDelayMs = 2 };
        var counters = new DeliveryCounters();

        var alias = SignalAliases.Of(typeof(TestSignal));
        using var delivery = new Delivery(sub, journal, view, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize();
        await delivery.Ensure(pub, [alias], DateTimeOffset.UtcNow);

        // Populate 50 signals
        for (var i = 1; i <= 50; i++)
        {
            var js = new JournaledSignal(pub, i, new TestSignal(i), DateTimeOffset.UtcNow);
            view.Signals.Add(new PositionedSignal(new Position(i), alias, js));
        }
        view.Last = new Position(50);

        // Fire 100 concurrent tasks calling Nudged with various positions
        var tasks = Enumerable.Range(1, 100).Select(i => Task.Run(async () =>
        {
            var pos = (i % 50) + 1;
            await delivery.Nudged(new Position(pos));
        })).ToArray();

        await Task.WhenAll(tasks);
        await delivery.CatchUp(nudged: true);

        // Verification:
        // Concurrency level inside Consume must NEVER exceed 1
        Assert.Equal(1, consumer.MaxObservedConcurrency);
        // All 50 signals must have been consumed exactly once
        Assert.Equal(50, consumer.Consumed.Count);
        // Ordering must be strictly preserved
        var list = consumer.Consumed.ToList();
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(i + 1, ((TestSignal)list[i].Signal).Number);
        }
    }

    [Fact]
    public async Task Nudge_arriving_during_flush_does_not_lose_signals()
    {
        var sub = NeuronId.Parse("b1/sub/flushrace");
        var pub = NeuronId.Parse("b1/pub/flushrace");
        var journal = new MockJournal(sub.Brain);
        var view = new DynamicJournalView();
        var consumer = new ConcurrentConsumer();
        var counters = new DeliveryCounters();

        var testAlias = SignalAliases.Of(typeof(TestSignal));
        var ignoredAlias = "unwanted.alias";
        using var delivery = new Delivery(sub, journal, view, consumer, a => a == testAlias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize();
        await delivery.Ensure(pub, [testAlias], DateTimeOffset.UtcNow);

        // Stage an unwantedsignal (so no reaction runs, but cursor advances, and it hits line 103 Flush)
        var js1 = new JournaledSignal(pub, 1, new OtherSignal("ignore"), DateTimeOffset.UtcNow);
        view.Signals.Add(new PositionedSignal(new Position(1), ignoredAlias, js1));
        view.Last = new Position(1);

        // Set up Flush hook so we pause inside Flush at line 103
        journal.FlushAwaited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        journal.FlushContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Start CatchUp in background — it will advance past unwantedsignal, finish do..while(_more), and await Flush(force: true) at line 103
        var catchUpTask = Task.Run(() => delivery.CatchUp(nudged: true));

        // Wait until catchUp enters Flush on line 103
        await journal.FlushAwaited.Task;

        // While line 103 Flush is paused, add a WANTED signal 2 to the journal and trigger concurrent CatchUp
        var js2 = new JournaledSignal(pub, 2, new TestSignal(2), DateTimeOffset.UtcNow);
        view.Signals.Add(new PositionedSignal(new Position(2), testAlias, js2));
        view.Last = new Position(2);

        // Concurrent call to CatchUp while previous CatchUp is stuck in Flush on line 103
        var concurrentCatchUp = Task.Run(() => delivery.CatchUp(nudged: true));

        // Wait to ensure concurrentCatchUp has hit lock(_gate) { if (_running) { _more = true; return; } }
        await Task.Delay(50);
        await concurrentCatchUp; // This will return immediately because _running is true!

        // Now resume line 103 Flush
        journal.FlushContinue.SetResult();
        await catchUpTask; // First catch-up exits!

        // CRITICAL CHECK:
        // Because concurrentCatchUp set _more = true while catchUp was on line 103 (past while(_more)),
        // catchUp did NOT re-run Pump! And concurrentCatchUp returned without running Pump!
        // Signal 2 was LOST and never consumed!
        Assert.Contains(consumer.Consumed, s => s.Signal is TestSignal ts && ts.Number == 2);
    }

    [Fact]
    public async Task Consumer_failure_unfreezes_delivery_and_retries_on_next_nudge()
    {
        var sub = NeuronId.Parse("b1/sub/fault");
        var pub = NeuronId.Parse("b1/pub/fault");
        var journal = new MockJournal(sub.Brain);
        var view = new DynamicJournalView();
        var consumer = new ConcurrentConsumer { ThrowFirstTime = true };
        var counters = new DeliveryCounters();

        var alias = SignalAliases.Of(typeof(TestSignal));
        using var delivery = new Delivery(sub, journal, view, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize();
        await delivery.Ensure(pub, [alias], DateTimeOffset.UtcNow);

        var js1 = new JournaledSignal(pub, 1, new TestSignal(100), DateTimeOffset.UtcNow);
        view.Signals.Add(new PositionedSignal(new Position(1), alias, js1));
        view.Last = new Position(1);

        // First catch up fails because ThrowFirstTime is true
        await delivery.CatchUp(nudged: true);
        Assert.Equal(1, counters.ReactionFailures);
        Assert.Empty(consumer.Consumed);

        // Subsequent nudge must NOT be blocked and must successfully deliver
        await delivery.Nudged(new Position(1));
        Assert.Single(consumer.Consumed);
        Assert.Equal(100, ((TestSignal)consumer.Consumed.First().Signal).Number);
    }

    // =========================================================================
    // 2. Retention Floor & Gap Handling Stress Tests
    // =========================================================================

    [Fact]
    public async Task Retention_gap_translation_produces_valid_sequence_range()
    {
        var sub = NeuronId.Parse("b1/sub/gaptest");
        var pub = NeuronId.Parse("b1/pub/gaptest");
        var journal = new MockJournal(sub.Brain);
        var view = new DynamicJournalView();
        var consumer = new ConcurrentConsumer();
        var counters = new DeliveryCounters();

        var alias = SignalAliases.Of(typeof(TestSignal));
        using var delivery = new Delivery(sub, journal, view, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize();
        await delivery.Ensure(pub, [alias], DateTimeOffset.UtcNow);

        // First, subscriber consumes a signal from pub that was appended at brain position 42 (sequence 2 of pub)
        var js1 = new JournaledSignal(pub, 2, new TestSignal(2), DateTimeOffset.UtcNow);
        view.Signals.Add(new PositionedSignal(new Position(42), alias, js1));
        view.Last = new Position(42);
        view.LastOfMap[pub] = new Position(42);
        await delivery.CatchUp(nudged: true);

        Assert.Single(consumer.Consumed);
        Assert.Equal(new Position(42), delivery.Subscriptions.Of(pub)!.Cursor);

        // Now, history is trimmed behind a checkpoint.
        // Brain Position floor moves to 45.
        // At brain position 45, publisher sequence is 3.
        view.Floors[pub] = new Position(45);
        view.SequenceAtMap[pub] = new Dictionary<Position, long>
        {
            [new Position(42)] = 2,
            [new Position(45)] = 3
        };
        view.Last = new Position(50);
        view.LastOfMap[pub] = new Position(50);

        // Trigger CatchUp to process the trimmed history gap
        await delivery.CatchUp(nudged: true);

        // Verify Gap signal delivered
        var gapSignal = consumer.Consumed.FirstOrDefault(s => s.Signal is Gap);
        Assert.NotNull(gapSignal);

        var gap = (Gap)gapSignal.Signal;
        Assert.Equal(pub, gap.Source);

        // CRITICAL CHECK: Gap.From and Gap.To
        // According to the domain contract, Gap represents missing publisher sequences:
        // Gap(Source, From, To) where From <= To.
        // If From is position (42) and To is sequence (3), then From (42) > To (3) which violates the invariant!
        // Both From and To MUST be publisher sequence numbers!
        Assert.True(gap.From <= gap.To, $"Domain invariant violated: Gap.From ({gap.From}) must be <= Gap.To ({gap.To}). From is raw brain position, not publisher sequence!");
    }

    [Fact]
    public async Task Brain_wide_subscription_gap_reports_correct_source()
    {
        var sub = NeuronId.Parse("b1/sub/widegap");
        var journal = new MockJournal(sub.Brain);
        var view = new DynamicJournalView();
        var consumer = new ConcurrentConsumer();
        var counters = new DeliveryCounters();

        var alias = SignalAliases.Of(typeof(TestSignal));
        using var delivery = new Delivery(sub, journal, view, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize();
        await delivery.EnsureBrainWide([alias], DateTimeOffset.UtcNow, exact: false);

        // History floor is moved to 20 for brain-wide
        view.Floors[sub] = new Position(20);
        view.Floors[NeuronId.Parse("b1/pub/dummy")] = new Position(20);
        // Set dynamic view default floor
        view.DefaultFloor = new Position(20);
        view.Last = new Position(30);

        await delivery.CatchUp(nudged: true);

        var gapSignal = consumer.Consumed.FirstOrDefault(s => s.Signal is Gap);
        Assert.NotNull(gapSignal);
        var gap = (Gap)gapSignal.Signal;
        // What is gap.Source when subscription is brain-wide?
        // Should NOT report the subscriber itself as the source of the gap!
        Assert.NotEqual(sub, gap.Source);
    }

    [Fact]
    public async Task Ensure_does_not_replay_pre_subscription_signals_when_other_publishers_exist()
    {
        var brain = new BrainId("b1");
        var time = TimeProvider.System;
        var journal = new MemoryJournal(brain, time);
        var sub = NeuronId.Parse("b1/sub/ensure");
        var pubA = NeuronId.Parse("b1/pub/a");
        var pubB = NeuronId.Parse("b1/pub/b");
        var consumer = new ConcurrentConsumer();
        var counters = new DeliveryCounters();
        var alias = SignalAliases.Of(typeof(TestSignal));

        // 1. PubA publishes 10 signals: Positions 1..10, Sequences 1..10
        for (var i = 1; i <= 10; i++)
        {
            var commitA = new Commit(pubA, null, [new TestSignal(i)], null, new Stamp(null, null));
            await journal.Append(commitA);
        }

        // 2. PubB publishes 5 signals: Positions 11..15, Sequences 1..5
        for (var i = 1; i <= 5; i++)
        {
            var commitB = new Commit(pubB, null, [new TestSignal(i)], null, new Stamp(null, null));
            await journal.Append(commitB);
        }

        // 3. Now Sub subscribes to PubB!
        using var delivery = new Delivery(sub, journal, journal, consumer, a => a == alias ? typeof(TestSignal) : null, time, counters);
        await delivery.Initialize();
        await delivery.Ensure(pubB, [alias], DateTimeOffset.UtcNow);

        // 4. Run catch up
        await delivery.CatchUp(nudged: true);

        // Pre-subscription signals MUST NOT be delivered!
        Assert.Empty(consumer.Consumed);
    }

    // =========================================================================
    // 3. Nudge & Fanout Stress Tests
    // =========================================================================

    private sealed class MockNudge : INudge
    {
        public ConcurrentBag<(NeuronId Subscriber, Position UpTo)> Nudges { get; } = [];
        public void Nudge(NeuronId subscriber, Position upTo) => Nudges.Add((subscriber, upTo));
    }

    [Fact]
    public async Task Fanout_appended_dispatches_only_to_interested_subscribers()
    {
        var view = new DynamicJournalView();
        var nudge = new MockNudge();
        var pub = NeuronId.Parse("b1/pub/fanout");
        var subInterested = NeuronId.Parse("b1/sub/interested");
        var subIgnored = NeuronId.Parse("b1/sub/ignored");

        var testAlias = SignalAliases.Of(typeof(TestSignal));
        var otherAlias = SignalAliases.Of(typeof(OtherSignal));

        view.Subscriptions.Fold(subInterested, Subscriptions.None(subInterested).Add(pub, [testAlias], new Position(0), DateTimeOffset.UtcNow).Encode(), StateChange.Write);
        view.Subscriptions.Fold(subIgnored, Subscriptions.None(subIgnored).Add(pub, [otherAlias], new Position(0), DateTimeOffset.UtcNow).Encode(), StateChange.Write);

        var fanout = new Fanout(view, nudge, a => a == testAlias ? typeof(TestSignal) : typeof(OtherSignal), (_, _) => { });

        var jSignal = new JournaledSignal(pub, 1, new TestSignal(1), DateTimeOffset.UtcNow);
        await fanout.Appended(pub, [jSignal], new Position(10), CancellationToken.None);

        Assert.Single(nudge.Nudges);
        Assert.Equal(subInterested, nudge.Nudges.First().Subscriber);
        Assert.Equal(new Position(10), nudge.Nudges.First().UpTo);
    }

    [Fact]
    public void Fanout_reconcile_nudges_subscribers_behind_slack_threshold()
    {
        var view = new DynamicJournalView();
        var nudge = new MockNudge();
        var pub = NeuronId.Parse("b1/pub/reconcile");
        var subBehind = NeuronId.Parse("b1/sub/behind");
        var subUpToDate = NeuronId.Parse("b1/sub/uptodate");

        var testAlias = SignalAliases.Of(typeof(TestSignal));

        // SubBehind has cursor 0 while journal last is 2000 (exceeds SkipFlush=1000)
        view.Subscriptions.Fold(subBehind, Subscriptions.None(subBehind).Add(pub, [testAlias], new Position(0), DateTimeOffset.UtcNow).Encode(), StateChange.Write);
        view.Subscriptions.Fold(subUpToDate, Subscriptions.None(subUpToDate).Add(pub, [testAlias], new Position(2000), DateTimeOffset.UtcNow).Encode(), StateChange.Write);

        view.Last = new Position(2000);
        view.LastOfMap[pub] = new Position(2000);

        var fanout = new Fanout(view, nudge, a => a == testAlias ? typeof(TestSignal) : null, (_, _) => { });
        var count = fanout.Reconcile();

        Assert.Equal(1, count);
        Assert.Single(nudge.Nudges);
        Assert.Equal(subBehind, nudge.Nudges.First().Subscriber);
        Assert.Equal(new Position(2000), nudge.Nudges.First().UpTo);
    }
}
