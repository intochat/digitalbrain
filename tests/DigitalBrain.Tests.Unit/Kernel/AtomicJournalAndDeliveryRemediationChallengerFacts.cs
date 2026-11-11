#pragma warning disable CA2201
#pragma warning disable CA1822
#pragma warning disable CA1040
#pragma warning disable CA1859
#pragma warning disable xUnit1051
using System.Collections.Concurrent;
using System.Text;
using DigitalBrain.Kernel;
using DigitalBrain.Signals;
using DigitalBrain.Spi;

namespace DigitalBrain.Tests.Unit.Kernel;

/// <summary>
/// Empirical adversarial challenger facts for Milestone 3 Remediation Iteration 2:
/// 1. Verifies that Journal<TSignal> updates both sequence and global brain Position upon appending signals,
///    and IJournalView.LastOf accurately returns that position.
/// 2. Verifies that concurrent flushes and nudges in Delivery.cs don't deadlock or drop signals.
/// </summary>
public sealed class AtomicJournalAndDeliveryRemediationChallengerFacts
{
    private sealed record TestSignal(int Number) : Signal;

    // =========================================================================
    // 1. Multi-Publisher Position and Sequence Tracking in Journal<TSignal> & IJournalView
    // =========================================================================

    [Fact]
    public async Task Journal_and_view_accurately_tracks_interleaved_publisher_positions_and_sequences()
    {
        var brain = new BrainId("b1");
        var time = TimeProvider.System;
        var journal = new MemoryJournal(brain, time);
        IJournalView view = journal;

        var p1 = NeuronId.Parse("b1/pub/p1");
        var p2 = NeuronId.Parse("b1/pub/p2");
        var p3 = NeuronId.Parse("b1/pub/p3");

        // Initial state: No signals
        Assert.Equal(new Position(0), view.Last);
        Assert.Equal(new Position(0), view.LastOf(p1));
        Assert.Equal(new Position(0), view.LastOf(p2));
        Assert.Equal(new Position(0), view.LastOf(p3));

        // Step 1: P1 appends 3 signals (Signals 1, 2, 3 for P1)
        var c1 = new Commit(p1, null, [new TestSignal(1), new TestSignal(2), new TestSignal(3)], null, new Stamp(null, null));
        var r1 = await journal.Append(c1);
        Assert.Equal(1L, r1.Version);
        Assert.Equal(1L, r1.FirstSequence);
        Assert.Equal(new Position(1), r1.FirstPosition);
        Assert.Equal(new Position(3), view.Last);
        Assert.Equal(new Position(3), view.LastOf(p1));
        Assert.Equal(new Position(0), view.LastOf(p2));

        // Step 2: P2 writes state ONLY (0 signals)
        var c2 = new Commit(p2, new StateWrite("cfg", StateChange.Write, "{\"x\":1}"u8.ToArray(), Expected: null), [], null, new Stamp(null, null));
        var r2 = await journal.Append(c2);
        Assert.Equal(2L, r2.Version);
        // Position must NOT advance when 0 signals are committed!
        Assert.Equal(new Position(3), view.Last);
        Assert.Equal(new Position(3), view.LastOf(p1));
        Assert.Equal(new Position(0), view.LastOf(p2));

        // Step 3: P2 appends 2 signals (Signals 1, 2 for P2)
        var c3 = new Commit(p2, null, [new TestSignal(10), new TestSignal(20)], null, new Stamp(null, null));
        var r3 = await journal.Append(c3);
        Assert.Equal(3L, r3.Version);
        Assert.Equal(1L, r3.FirstSequence);
        Assert.Equal(new Position(4), r3.FirstPosition);
        Assert.Equal(new Position(5), view.Last);
        Assert.Equal(new Position(3), view.LastOf(p1)); // P1 position must remain 3!
        Assert.Equal(new Position(5), view.LastOf(p2)); // P2 position is now 5!
        Assert.Equal(new Position(0), view.LastOf(p3));

        // Step 4: P3 appends 1 signal (Signal 1 for P3)
        var c4 = new Commit(p3, null, [new TestSignal(100)], null, new Stamp(null, null));
        var r4 = await journal.Append(c4);
        Assert.Equal(4L, r4.Version);
        Assert.Equal(1L, r4.FirstSequence);
        Assert.Equal(new Position(6), r4.FirstPosition);
        Assert.Equal(new Position(6), view.Last);
        Assert.Equal(new Position(3), view.LastOf(p1));
        Assert.Equal(new Position(5), view.LastOf(p2));
        Assert.Equal(new Position(6), view.LastOf(p3));

        // Step 5: P1 appends 2 more signals (Signals 4, 5 for P1)
        var c5 = new Commit(p1, null, [new TestSignal(4), new TestSignal(5)], null, new Stamp(null, null));
        var r5 = await journal.Append(c5);
        Assert.Equal(5L, r5.Version);
        Assert.Equal(4L, r5.FirstSequence);
        Assert.Equal(new Position(7), r5.FirstPosition);
        Assert.Equal(new Position(8), view.Last);
        Assert.Equal(new Position(8), view.LastOf(p1)); // P1 advanced to 8
        Assert.Equal(new Position(5), view.LastOf(p2)); // P2 remains 5
        Assert.Equal(new Position(6), view.LastOf(p3)); // P3 remains 6

        // Verify SequenceAt and PositionAt for every signal across all publishers
        // P1: Positions 1, 2, 3 -> Seq 1, 2, 3; Positions 7, 8 -> Seq 4, 5
        Assert.Equal(1L, view.SequenceAt(p1, new Position(1)));
        Assert.Equal(2L, view.SequenceAt(p1, new Position(2)));
        Assert.Equal(3L, view.SequenceAt(p1, new Position(3)));
        Assert.Equal(4L, view.SequenceAt(p1, new Position(7)));
        Assert.Equal(5L, view.SequenceAt(p1, new Position(8)));

        Assert.Equal(new Position(1), journal.PositionAt(p1, 1));
        Assert.Equal(new Position(2), journal.PositionAt(p1, 2));
        Assert.Equal(new Position(3), journal.PositionAt(p1, 3));
        Assert.Equal(new Position(7), journal.PositionAt(p1, 4));
        Assert.Equal(new Position(8), journal.PositionAt(p1, 5));

        // P2: Positions 4, 5 -> Seq 1, 2
        Assert.Equal(1L, view.SequenceAt(p2, new Position(4)));
        Assert.Equal(2L, view.SequenceAt(p2, new Position(5)));
        Assert.Equal(new Position(4), journal.PositionAt(p2, 1));
        Assert.Equal(new Position(5), journal.PositionAt(p2, 2));

        // P3: Position 6 -> Seq 1
        Assert.Equal(1L, view.SequenceAt(p3, new Position(6)));
        Assert.Equal(new Position(6), journal.PositionAt(p3, 1));

        // Test Trimming of P1: Trim up to sequence 3 (position 3)
        journal.Trim(p1, floorSequence: 3, floorPosition: 3);
        Assert.Equal(new Position(3), view.FloorOf(p1));
        Assert.Equal(new Position(8), view.LastOf(p1)); // LastOf remains 8 after trim

        // Trimmed coordinates must still resolve accurately via SequenceAt / PositionAt!
        Assert.Equal(1L, view.SequenceAt(p1, new Position(1)));
        Assert.Equal(2L, view.SequenceAt(p1, new Position(2)));
        Assert.Equal(3L, view.SequenceAt(p1, new Position(3)));
        Assert.Equal(new Position(1), journal.PositionAt(p1, 1));
        Assert.Equal(new Position(2), journal.PositionAt(p1, 2));
        Assert.Equal(new Position(3), journal.PositionAt(p1, 3));

        // Snapshot and restore verification
        var snapshot = new MemoryJournal(brain, time);
        snapshot.RestoreSnapshot(journal);
        Assert.Equal(view.Last, snapshot.Last);
        Assert.Equal(view.LastOf(p1), snapshot.LastOf(p1));
        Assert.Equal(view.LastOf(p2), snapshot.LastOf(p2));
        Assert.Equal(view.LastOf(p3), snapshot.LastOf(p3));
    }

    // =========================================================================
    // 2. Concurrent Flushes and Nudges in Delivery.cs
    // =========================================================================

    private sealed class SlowFlushJournal(BrainId brain, int delayMs = 30) : IBrainJournal
    {
        public BrainId Brain => brain;
        public List<Commit> Commits { get; } = [];
        public int DelayMs { get; set; } = delayMs;

        public async Task<CommitResult> Append(Commit commit, CancellationToken ct = default)
        {
            if (DelayMs > 0)
            {
                await Task.Delay(DelayMs, ct).ConfigureAwait(false);
            }
            lock (Commits)
            {
                Commits.Add(commit);
                return new CommitResult(Commits.Count, 1, new Position(Commits.Count), DateTimeOffset.UtcNow);
            }
        }

        public Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
            => Task.FromResult<JournalRecord?>(null);

        public Task<SignalPage> Read(long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class TrackingConsumer : ISignalConsumer
    {
        public bool TakesUntyped => false;
        public ConcurrentQueue<JournaledSignal> Consumed { get; } = new();
        public int Concurrency;
        public int MaxConcurrency;

        public Task Consume(JournaledSignal signal, Func<IDisposable> enter, CancellationToken ct)
        {
            var cur = Interlocked.Increment(ref Concurrency);
            int initialMax;
            do
            {
                initialMax = MaxConcurrency;
                if (cur <= initialMax) { break; }
            } while (Interlocked.CompareExchange(ref MaxConcurrency, cur, initialMax) != initialMax);

            try
            {
                using var scope = enter();
                Consumed.Enqueue(signal);
                return Task.CompletedTask;
            }
            finally
            {
                Interlocked.Decrement(ref Concurrency);
            }
        }
    }

    [Fact]
    public async Task Concurrent_nudges_during_slow_flush_never_deadlock_or_drop_signals()
    {
        var brain = new BrainId("b1");
        var sub = NeuronId.Parse("b1/sub/slowflush");
        var pub = NeuronId.Parse("b1/pub/slowflush");

        var slowJournal = new SlowFlushJournal(brain, delayMs: 40);
        var memoryJournal = new MemoryJournal(brain, TimeProvider.System);
        var consumer = new TrackingConsumer();
        var counters = new DeliveryCounters();
        var alias = SignalAliases.Of(typeof(TestSignal));

        using var delivery = new Delivery(sub, slowJournal, memoryJournal, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize();
        await delivery.Ensure(pub, [alias], DateTimeOffset.UtcNow);

        const int totalSignals = 30;
        var appendTask = Task.Run(async () =>
        {
            for (var i = 1; i <= totalSignals; i++)
            {
                var commit = new Commit(pub, null, [new TestSignal(i)], null, new Stamp(null, null));
                await memoryJournal.Append(commit);
                await Task.Delay(5);
            }
        });

        // Concurrently bombard Delivery with Nudged calls across 15 parallel tasks
        var nudgeTasks = Enumerable.Range(1, 15).Select(taskIdx => Task.Run(async () =>
        {
            for (var i = 1; i <= totalSignals; i++)
            {
                await delivery.Nudged(new Position(i));
                await Task.Delay(3);
            }
        })).ToArray();

        await Task.WhenAll(appendTask, Task.WhenAll(nudgeTasks));

        // Final catch up to ensure all delivered
        await delivery.CatchUp(nudged: true);

        // Verification:
        // 1. Single flight pump guaranteed
        Assert.Equal(1, consumer.MaxConcurrency);

        // 2. Exactly totalSignals consumed without loss
        Assert.Equal(totalSignals, consumer.Consumed.Count);

        // 3. Strict in-order processing
        var consumedList = consumer.Consumed.ToList();
        for (var i = 0; i < totalSignals; i++)
        {
            Assert.Equal(i + 1, ((TestSignal)consumedList[i].Signal).Number);
        }

        // 4. Zero deadlocks, zero failures
        Assert.Equal(0, counters.ReactionFailures);
    }

    [Fact]
    public async Task Massive_concurrent_CatchUp_and_Nudged_races_execute_safely_without_deadlock()
    {
        var brain = new BrainId("b1");
        var sub = NeuronId.Parse("b1/sub/race");
        var pub = NeuronId.Parse("b1/pub/race");

        var journal = new MemoryJournal(brain, TimeProvider.System);
        var consumer = new TrackingConsumer();
        var counters = new DeliveryCounters();
        var alias = SignalAliases.Of(typeof(TestSignal));

        using var delivery = new Delivery(sub, journal, journal, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize();
        await delivery.Ensure(pub, [alias], DateTimeOffset.UtcNow);

        // Pre-commit 40 signals
        for (var i = 1; i <= 40; i++)
        {
            await journal.Append(new Commit(pub, null, [new TestSignal(i)], null, new Stamp(null, null)));
        }

        // Fire 30 concurrent tasks mixing CatchUp(nudged: true) and Nudged(pos)
        var barrier = new Barrier(30);
        var tasks = Enumerable.Range(0, 30).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            if (i % 2 == 0)
            {
                await delivery.Nudged(new Position((i % 40) + 1));
            }
            else
            {
                await delivery.CatchUp(nudged: true);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        await delivery.CatchUp(nudged: true);

        // Assert all 40 consumed, exactly once, single flight
        Assert.Equal(1, consumer.MaxConcurrency);
        Assert.Equal(40, consumer.Consumed.Count);
        Assert.Equal(0, counters.ReactionFailures);
    }

    [Fact]
    public async Task Concurrent_multi_publisher_commits_maintain_strict_sequence_and_position_invariants()
    {
        var brain = new BrainId("b1");
        var journal = new MemoryJournal(brain, TimeProvider.System);
        IJournalView view = journal;

        var publishers = new[]
        {
            NeuronId.Parse("b1/pub/conc-1"),
            NeuronId.Parse("b1/pub/conc-2"),
            NeuronId.Parse("b1/pub/conc-3")
        };

        const int signalsPerPublisher = 50;
        var barrier = new Barrier(publishers.Length);

        // Run concurrent publishers appending signals
        var tasks = publishers.Select(pub => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            for (var i = 1; i <= signalsPerPublisher; i++)
            {
                var commit = new Commit(pub, null, [new TestSignal(i)], null, new Stamp(null, null));
                await journal.Append(commit);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Verification:
        // 1. Total signals = 150
        var totalExpected = publishers.Length * signalsPerPublisher;
        Assert.Equal(new Position(totalExpected), view.Last);

        // 2. Each publisher must have exactly signalsPerPublisher sequences
        foreach (var pub in publishers)
        {
            Assert.Equal(signalsPerPublisher, journal.LastSequenceOf(pub));
            var lastPos = view.LastOf(pub).Value;
            Assert.True(lastPos > 0 && lastPos <= totalExpected);
        }

        // 3. Read brain-wide signals and check for unique, strictly increasing positions 1..150
        var allSignals = await view.ReadAfter(new Position(0), null, totalExpected + 10, CancellationToken.None);
        Assert.Equal(totalExpected, allSignals.Signals.Count);
        for (var i = 0; i < totalExpected; i++)
        {
            Assert.Equal(i + 1, allSignals.Signals[i].Position.Value);
        }

        // 4. Verify publisher-specific reads yield strictly contiguous sequences 1..50
        foreach (var pub in publishers)
        {
            var pubPage = await view.ReadAfter(new Position(0), pub, signalsPerPublisher + 10, CancellationToken.None);
            Assert.Equal(signalsPerPublisher, pubPage.Signals.Count);
            for (var seq = 1; seq <= signalsPerPublisher; seq++)
            {
                Assert.Equal(seq, pubPage.Signals[seq - 1].Journaled.Sequence);
            }
        }
    }

    [Fact]
    public async Task Delivery_recovers_cleanly_after_gap_and_continues_processing_new_signals()
    {
        var brain = new BrainId("b1");
        var sub = NeuronId.Parse("b1/sub/gapfollowup");
        var pub = NeuronId.Parse("b1/pub/gapfollowup");

        var journal = new MemoryJournal(brain, TimeProvider.System);
        var consumer = new TrackingConsumer();
        var counters = new DeliveryCounters();
        var alias = SignalAliases.Of(typeof(TestSignal));

        using var delivery = new Delivery(sub, journal, journal, consumer, a => a == alias ? typeof(TestSignal) : null, TimeProvider.System, counters);
        await delivery.Initialize();
        await delivery.Ensure(pub, [alias], DateTimeOffset.UtcNow);

        // Append 10 signals to journal
        for (var i = 1; i <= 10; i++)
        {
            await journal.Append(new Commit(pub, null, [new TestSignal(i)], null, new Stamp(null, null)));
        }

        // Catch up on initial 10 signals
        await delivery.CatchUp(nudged: true);
        Assert.Equal(10, consumer.Consumed.Count);

        // Now trim the journal past the subscriber's cursor:
        // Subscriber is at position 10. Trim up to sequence 15 (position 15).
        // First append 10 more signals (positions 11..20)
        for (var i = 11; i <= 20; i++)
        {
            await journal.Append(new Commit(pub, null, [new TestSignal(i)], null, new Stamp(null, null)));
        }

        // Now trim up to sequence 15 (position 15)
        journal.Trim(pub, floorSequence: 15, floorPosition: 15);

        // CatchUp must now observe the gap, deliver a Gap signal, and then consume signals 16..20!
        await delivery.CatchUp(nudged: true);

        // Verification:
        // Gap signal was received
        var gapSignal = consumer.Consumed.FirstOrDefault(s => s.Signal is Gap);
        Assert.NotNull(gapSignal);
        var gap = (Gap)gapSignal.Signal;
        Assert.Equal(pub, gap.Source);
        Assert.True(gap.From <= gap.To, $"Gap range invalid: {gap.From} > {gap.To}");

        // And the new signals after the floor (16..20) were also delivered
        var deliveredTestSignals = consumer.Consumed.Where(s => s.Signal is TestSignal).Select(s => ((TestSignal)s.Signal).Number).ToList();
        Assert.Contains(16, deliveredTestSignals);
        Assert.Contains(20, deliveredTestSignals);
    }
}
