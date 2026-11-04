using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain;
using DigitalBrain.Runtime.Orleans.Log;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

/// <summary>
/// Empirical adversarial challenge suite stress-testing Signal Log Pub/Sub:
/// 1. Concurrency, monotonic sequence, zero-gap and zero-collision guarantees.
/// 2. Dropped notification resilience and multi-page catch-up without skips or re-execution.
/// 3. Reaction fault boundary, retry exhaustion, dead-letter recording, and unblocked stream progression.
/// 4. Log compaction safety, subscriber lag blocking, staleness quarantine (>7 days), and boundary conditions.
/// </summary>
public sealed class SignalLogAdversarialFacts
{
    private sealed record TestSubscriberState : NeuronState
    {
        public List<int> ProcessedValues { get; init; } = [];
        public List<string> ProcessedNotes { get; init; } = [];
    }

    #region 1. Empirical Monotonic Sequence Verification

    [Fact]
    public async Task Interleaved_concurrent_saves_generate_strictly_monotonic_sequences_without_gaps_or_collisions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        const int GrainCount = 20;
        const int WritesPerGrain = 10;
        var grainIds = Enumerable.Range(1, GrainCount)
            .Select(i => GrainId.Create("neuron.host", $"b1/test.neuron/{i}"))
            .ToArray();

        var returnedPositions = new ConcurrentBag<(string Grain, long Position)>();
        var totalSignalsAppended = 0;

        var tasks = new List<Task>();

        for (var g = 0; g < GrainCount; g++)
        {
            var grainId = grainIds[g];
            var currentGrainIndex = g + 1;
            tasks.Add(Task.Run(async () =>
            {
                var grainState = new TestGrainState<TestSubscriberState>
                {
                    State = new TestSubscriberState { SignalCursor = 0 }
                };

                for (var w = 1; w <= WritesPerGrain; w++)
                {
                    ct.ThrowIfCancellationRequested();
                    var count = (currentGrainIndex + w) % 4; // 0, 1, 2, or 3 signals
                    var signals = Enumerable.Range(1, count)
                        .Select(s => new Note($"g{currentGrainIndex}-w{w}-s{s}"))
                        .ToArray();

                    long pos;
                    if (w % 2 == 0)
                    {
                        pos = await store.Storage.WriteStateAsync("state", grainId, grainState, grainId.Key.ToString()!, signals, null);
                    }
                    else
                    {
                        pos = await store.Storage.AppendSignalsAsync("state", grainId, grainId.Key.ToString()!, signals);
                    }

                    Interlocked.Add(ref totalSignalsAppended, count);
                    returnedPositions.Add((grainId.Key.ToString()!, pos));
                }
            }, ct));
        }

        await Task.WhenAll(tasks);

        var log = await store.Log("b1");
        Assert.Equal(totalSignalsAppended, log.LastPosition);

        // Read all signals back from the log in batches
        var retrievedSignals = new List<StoredSignal>();
        var cursor = 0L;
        while (cursor < log.LastPosition)
        {
            var slice = await log.BrainSignals(cursor, 100, ct);
            if (slice.Held.Length == 0) { break; }
            retrievedSignals.AddRange(slice.Held);
            cursor = slice.Held[^1].Position;
        }

        // Verify that every single signal has a unique, strictly contiguous monotonic sequence
        Assert.Equal(totalSignalsAppended, retrievedSignals.Count);
        for (var i = 0; i < retrievedSignals.Count; i++)
        {
            var expectedPosition = i + 1L;
            Assert.Equal(expectedPosition, retrievedSignals[i].Position);
        }

        // Verify zero collisions: positions must be strictly distinct
        var distinctPositions = retrievedSignals.Select(s => s.Position).ToHashSet();
        Assert.Equal(retrievedSignals.Count, distinctPositions.Count);
    }

    [Fact]
    public async Task High_concurrency_stress_burst_with_100_concurrent_writers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        const int WriterCount = 100;
        var positions = new ConcurrentBag<long>();

        var tasks = Enumerable.Range(1, WriterCount).Select(i => Task.Run(async () =>
        {
            var grainId = GrainId.Create("neuron.host", $"b1/burst.writer/{i}");
            var pos = await store.Storage.AppendSignalsAsync("state", grainId, grainId.Key.ToString()!, [new Count(i)]);
            positions.Add(pos);
        }, ct)).ToArray();

        await Task.WhenAll(tasks);

        var log = await store.Log("b1");
        Assert.Equal(WriterCount, log.LastPosition);

        var slice = await log.BrainSignals(0, WriterCount, ct);
        Assert.Equal(WriterCount, slice.Held.Length);

        // Verify 1..WriterCount contiguous positions
        var positionsList = slice.Held.Select(s => s.Position).ToList();
        var expectedPositions = Enumerable.Range(1, WriterCount).Select(i => (long)i).ToList();
        Assert.Equal(expectedPositions, positionsList);
    }

    #endregion

    #region 2. Dropped Notification Resilience

    [Fact]
    public async Task Dropped_notifications_healed_by_EnsureCaughtUpAsync_with_multi_page_backlog()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var publisher = NeuronId.Parse("b1/chat/bulk-publisher");
        var subscriber = NeuronId.Parse("b1/chat/offline-subscriber");
        var log = await store.Log("b1");

        // Emitting 2,500 signals exceeds SignalPage.MaxLimit (1,000), testing multi-page batching
        const int TotalSignals = 2500;
        const int BatchSize = 250;
        for (var b = 0; b < TotalSignals / BatchSize; b++)
        {
            var batch = Enumerable.Range(b * BatchSize + 1, BatchSize).Select(v => (Signal)new Count(v)).ToArray();
            await log.AppendEntry(BrainStore.GrainOf(publisher).ToString(), "state", null, BrainRecordChange.None, [],
                publisher.ToString(), batch, ct);
        }

        Assert.Equal(TotalSignals, log.LastPosition);

        // Subscriber was offline during all notifications (100% notification drop)
        var state = new TestSubscriberState { SignalCursor = 0 };
        var commits = 0;
        var signalLog = new BrainSignalLog("b1", store.Logs);

        var caughtUpState = await signalLog.EnsureCaughtUpAsync(
            subscriber,
            state,
            (s, sig, token) =>
            {
                var val = sig is Count c ? c.N : -1;
                s.ProcessedValues.Add(val);
                return Task.FromResult(s);
            },
            (s, token) =>
            {
                Interlocked.Increment(ref commits);
                return Task.CompletedTask;
            },
            targetPosition: TotalSignals,
            ct: ct);

        // Verify all 2500 signals processed in exact order
        Assert.Equal(TotalSignals, caughtUpState.SignalCursor);
        Assert.Equal(TotalSignals, caughtUpState.ProcessedValues.Count);
        Assert.Equal(1, caughtUpState.ProcessedValues.First());
        Assert.Equal(TotalSignals, caughtUpState.ProcessedValues.Last());

        for (var i = 0; i < TotalSignals; i++)
        {
            Assert.Equal(i + 1, caughtUpState.ProcessedValues[i]);
        }

        // Verify zero re-execution on redundant catch-up
        var reRunCommits = 0;
        var reRunState = await signalLog.EnsureCaughtUpAsync(
            subscriber,
            caughtUpState,
            (s, sig, token) => throw new InvalidOperationException("Should never be invoked on caught-up subscriber"),
            (s, token) => { reRunCommits++; return Task.CompletedTask; },
            targetPosition: TotalSignals,
            ct: ct);

        Assert.Equal(TotalSignals, reRunState.SignalCursor);
        Assert.Equal(0, reRunCommits);
    }

    [Fact]
    public async Task Incremental_signals_interleaved_with_repeated_EnsureCaughtUpAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var publisher = NeuronId.Parse("b1/chat/p-incremental");
        var subscriber = NeuronId.Parse("b1/chat/sub-incremental");
        var log = await store.Log("b1");
        var signalLog = new BrainSignalLog("b1", store.Logs);

        var state = new TestSubscriberState { SignalCursor = 0 };

        // Phase 1: 50 signals
        await log.AppendEntry(BrainStore.GrainOf(publisher).ToString(), "state", null, BrainRecordChange.None, [],
            publisher.ToString(), Enumerable.Range(1, 50).Select(v => (Signal)new Count(v)).ToArray(), ct);

        state = await signalLog.EnsureCaughtUpAsync(
            subscriber, state,
            (s, sig, token) => Task.FromResult(s with { ProcessedValues = [.. s.ProcessedValues, ((Count)sig).N] }),
            (s, token) => Task.CompletedTask,
            targetPosition: log.LastPosition, ct: ct);

        Assert.Equal(50, state.SignalCursor);
        Assert.Equal(50, state.ProcessedValues.Count);

        // Phase 2: 75 more signals (51..125)
        await log.AppendEntry(BrainStore.GrainOf(publisher).ToString(), "state", null, BrainRecordChange.None, [],
            publisher.ToString(), Enumerable.Range(51, 75).Select(v => (Signal)new Count(v)).ToArray(), ct);

        state = await signalLog.EnsureCaughtUpAsync(
            subscriber, state,
            (s, sig, token) => Task.FromResult(s with { ProcessedValues = [.. s.ProcessedValues, ((Count)sig).N] }),
            (s, token) => Task.CompletedTask,
            targetPosition: log.LastPosition, ct: ct);

        Assert.Equal(125, state.SignalCursor);
        Assert.Equal(125, state.ProcessedValues.Count);
        Assert.Equal(Enumerable.Range(1, 125).ToList(), state.ProcessedValues);

        // Phase 3: Zero new signals
        var phase3Count = 0;
        state = await signalLog.EnsureCaughtUpAsync(
            subscriber, state,
            (s, sig, token) => { phase3Count++; return Task.FromResult(s); },
            (s, token) => Task.CompletedTask,
            targetPosition: log.LastPosition, ct: ct);

        Assert.Equal(125, state.SignalCursor);
        Assert.Equal(0, phase3Count);
        Assert.Equal(125, state.ProcessedValues.Count);
    }

    #endregion

    #region 3. Reaction Fault Boundary & Poison Pills

    [Fact]
    public async Task Permanent_exception_isolated_to_dead_letter_after_retries_and_cursor_advances()
    {
        var ct = TestContext.Current.CancellationToken;
        var attempts = 0;
        TestSubscriberState? committedState = null;

        var initial = new TestSubscriberState { SignalCursor = 20 };
        var signal = new Note("permanent-failure-signal");
        const long position = 21;
        const string publisher = "b1/faulty/source";

        var (finalState, succeeded) = await ReactionFaultBoundary.ExecuteReactionAsync(
            initial,
            position,
            publisher,
            signal,
            (s, sig, token) =>
            {
                attempts++;
                throw new NotSupportedException("Permanent poison payload exception");
            },
            (s, token) =>
            {
                committedState = s;
                return Task.CompletedTask;
            },
            time: TimeProvider.System,
            maxRetries: 3,
            backoff: attempt => TimeSpan.Zero,
            ct: ct);

        Assert.False(succeeded);
        Assert.Equal(3, attempts);
        Assert.Equal(position, finalState.SignalCursor);
        Assert.NotNull(committedState);
        Assert.Equal(position, committedState.SignalCursor);

        var deadLetter = Assert.Single(finalState.DeadLetters);
        Assert.Equal(position, deadLetter.Position);
        Assert.Equal(publisher, deadLetter.Publisher);
        Assert.Equal(typeof(Note).FullName, deadLetter.SignalType);
        Assert.Contains("Permanent poison payload exception", deadLetter.ErrorMessage);
    }

    [Fact]
    public async Task Poison_pill_in_stream_does_not_halt_or_corrupt_subsequent_valid_signals()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var publisher = NeuronId.Parse("b1/chat/adversarial-stream");
        var subscriber = NeuronId.Parse("b1/chat/resilient-subscriber");
        var log = await store.Log("b1");

        // Stream with mixed valid signals, transient errors, and fatal poison pills
        var signalPayloads = new Signal[]
        {
            new Note("valid-1"),
            new Note("valid-2"),
            new Note("poison-pill-fatal-1"),
            new Note("valid-3"),
            new Note("transient-error-recoverable"),
            new Note("valid-4"),
            new Note("poison-pill-fatal-2"),
            new Note("valid-5"),
            new Note("valid-6")
        };

        await log.AppendEntry(BrainStore.GrainOf(publisher).ToString(), "state", null, BrainRecordChange.None, [],
            publisher.ToString(), signalPayloads, ct);

        Assert.Equal(9L, log.LastPosition);

        var state = new TestSubscriberState { SignalCursor = 0 };
        var signalLog = new BrainSignalLog("b1", store.Logs);
        var transientAttempts = 0;

        var finalState = await signalLog.EnsureCaughtUpAsync(
            subscriber,
            state,
            (s, sig, token) =>
            {
                var note = (Note)sig;
                if (note.Text.StartsWith("poison-pill-fatal", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Fatal poison pill: {note.Text}");
                }

                if (note.Text == "transient-error-recoverable")
                {
                    transientAttempts++;
                    if (transientAttempts < 3)
                    {
                        throw new TimeoutException($"Transient network glitch attempt {transientAttempts}");
                    }
                }

                return Task.FromResult(s with
                {
                    ProcessedNotes = [.. s.ProcessedNotes, note.Text]
                });
            },
            (s, token) => Task.CompletedTask,
            targetPosition: 9,
            ct: ct);

        // Entire stream must be fully caught up to position 9
        Assert.Equal(9L, finalState.SignalCursor);

        // Exactly 7 valid signals succeeded
        var expectedNotes = new[] { "valid-1", "valid-2", "valid-3", "transient-error-recoverable", "valid-4", "valid-5", "valid-6" };
        Assert.Equal(expectedNotes, finalState.ProcessedNotes);

        // Exactly 2 fatal poison pills isolated in DeadLetters
        Assert.Equal(2, finalState.DeadLetters.Length);
        Assert.Equal(3L, finalState.DeadLetters[0].Position);
        Assert.Contains("poison-pill-fatal-1", finalState.DeadLetters[0].ErrorMessage);
        Assert.Equal(7L, finalState.DeadLetters[1].Position);
        Assert.Contains("poison-pill-fatal-2", finalState.DeadLetters[1].ErrorMessage);
    }

    [Fact]
    public async Task Fatal_process_exceptions_are_not_swallowed_as_poison_pills()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled token

        var initial = new TestSubscriberState { SignalCursor = 1 };
        var signal = new Note("cancel-test");

        // OperationCanceledException must propagate out and NOT be treated as a poison pill
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await ReactionFaultBoundary.ExecuteReactionAsync(
                initial,
                2,
                "b1/chat/p",
                signal,
                (s, sig, token) => throw new OperationCanceledException(token),
                (s, token) => Task.CompletedTask,
                time: TimeProvider.System,
                ct: cts.Token);
        });
    }

    #endregion

    #region 4. Log Compaction & Stale Subscribers

    [Fact]
    public void Compaction_floor_is_strictly_bounded_by_lagging_active_subscribers()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        var subscribers = new List<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)>
        {
            ("sub-lagging-active-1", 120, manualTime.Now.AddDays(-2)),
            ("sub-lagging-active-2", 450, manualTime.Now.AddHours(-6)),
            ("sub-up-to-date", 1000, manualTime.Now.AddMinutes(-5))
        };

        const long logHead = 1000;
        var floor = signalLog.ComputeCompactionFloor(logHead, subscribers, manualTime.Now);

        // Compaction floor must not pass 120, protecting unread signals for sub-lagging-active-1
        Assert.Equal(120, floor);
    }

    [Fact]
    public void Compaction_floor_advances_when_lagging_subscriber_exceeds_StaleCursorAfter_7_days()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        var subscribers = new List<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)>
        {
            // Abandoned subscriber stalled at cursor 50 for 7 days + 1 hour
            ("sub-abandoned", 50, manualTime.Now.AddDays(-7).AddHours(-1)),
            // Active subscribers
            ("sub-active-1", 400, manualTime.Now.AddDays(-1)),
            ("sub-active-2", 600, manualTime.Now.AddHours(-1))
        };

        Assert.True(signalLog.IsQuarantined(subscribers[0].LastActiveAt, manualTime.Now));
        Assert.False(signalLog.IsQuarantined(subscribers[1].LastActiveAt, manualTime.Now));
        Assert.False(signalLog.IsQuarantined(subscribers[2].LastActiveAt, manualTime.Now));

        const long logHead = 800;
        var floor = signalLog.ComputeCompactionFloor(logHead, subscribers, manualTime.Now);

        // Compaction floor unblocked: advances to min of active subscribers (400)
        Assert.Equal(400, floor);
    }

    [Fact]
    public void Compaction_quarantine_boundary_precision_at_exactly_7_days()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        // Subscriber at 6 days, 23 hours, 59 minutes, 59 seconds ago -> NOT quarantined
        var justBeforeBoundary = manualTime.Now.AddDays(-7).AddSeconds(1);
        Assert.False(signalLog.IsQuarantined(justBeforeBoundary, manualTime.Now));

        // Subscriber at 7 days, 0 hours, 0 minutes, 1 second ago -> QUARANTINED
        var justAfterBoundary = manualTime.Now.AddDays(-7).AddSeconds(-1);
        Assert.True(signalLog.IsQuarantined(justAfterBoundary, manualTime.Now));
    }

    [Fact]
    public void Compaction_floor_advances_to_log_head_when_all_subscribers_are_quarantined()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        var subscribers = new List<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)>
        {
            ("abandoned-1", 100, manualTime.Now.AddDays(-8)),
            ("abandoned-2", 200, manualTime.Now.AddDays(-14))
        };

        const long logHead = 1500;
        var floor = signalLog.ComputeCompactionFloor(logHead, subscribers, manualTime.Now);

        // When all subscribers are dead/quarantined, compaction floor advances to log head
        // preventing permanent disk exhaustion
        Assert.Equal(logHead, floor);
    }

    [Fact]
    public void Custom_StaleCursorAfter_threshold_is_honored()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        // Custom 3-day stale threshold
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(3) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        var subscriberTime = manualTime.Now.AddDays(-4); // 4 days ago
        Assert.True(signalLog.IsQuarantined(subscriberTime, manualTime.Now));

        var activeSubscriberTime = manualTime.Now.AddDays(-2); // 2 days ago
        Assert.False(signalLog.IsQuarantined(activeSubscriberTime, manualTime.Now));
    }

    #endregion

    #region 5. Additional Adversarial Edge Cases

    [Fact]
    public async Task Log_snapshot_and_recovery_preserves_monotonic_position_and_continuation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();

        // Phase 1: Write entries, checkpoint, write more
        await using (var store1 = new BrainStore(home.Path))
        {
            var log1 = await store1.Log("b1");
            var grain1 = GrainId.Create("neuron.host", "b1/recovery.writer/1");

            // Write 25 signals
            for (var i = 1; i <= 25; i++)
            {
                await store1.Storage.AppendSignalsAsync("state", grain1, grain1.Key.ToString()!, [new Count(i)]);
            }
            Assert.Equal(25L, log1.LastPosition);

            // Take checkpoint
            await log1.Checkpoint(ct);

            // Write 15 more signals (positions 26..40)
            for (var i = 26; i <= 40; i++)
            {
                await store1.Storage.AppendSignalsAsync("state", grain1, grain1.Key.ToString()!, [new Count(i)]);
            }
            Assert.Equal(40L, log1.LastPosition);
        }

        // Phase 2: Reopen from same disk directory, verify recovery preserves LastPosition=40
        await using (var store2 = new BrainStore(home.Path))
        {
            var log2 = await store2.Log("b1");
            Assert.Equal(40L, log2.LastPosition);

            // Write 10 additional signals (positions 41..50)
            var grain2 = GrainId.Create("neuron.host", "b1/recovery.writer/2");
            for (var i = 41; i <= 50; i++)
            {
                var pos = await store2.Storage.AppendSignalsAsync("state", grain2, grain2.Key.ToString()!, [new Count(i)]);
                Assert.Equal(i, pos);
            }

            Assert.Equal(50L, log2.LastPosition);

            // Read all 50 signals back and assert contiguous positions 1..50
            var slice = await log2.BrainSignals(0, 50, ct);
            Assert.Equal(50, slice.Held.Length);
            for (var i = 0; i < 50; i++)
            {
                Assert.Equal(i + 1L, slice.Held[i].Position);
            }
        }
    }

    [Fact]
    public async Task ReactionFaultBoundary_exact_attempt_and_delay_sequence_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var delaysRecorded = new List<TimeSpan>();
        var invocations = 0;

        var initial = new TestSubscriberState { SignalCursor = 0 };
        var signal = new Note("audit-retries");

        var (finalState, succeeded) = await ReactionFaultBoundary.ExecuteReactionAsync(
            initial,
            1,
            "b1/chat/p",
            signal,
            (s, sig, token) =>
            {
                invocations++;
                throw new InvalidOperationException("Fail forever");
            },
            (s, token) => Task.CompletedTask,
            time: TimeProvider.System,
            maxRetries: 3,
            backoff: attempt =>
            {
                var d = TimeSpan.FromMilliseconds(50 * attempt);
                delaysRecorded.Add(d);
                return d;
            },
            ct: ct);

        Assert.False(succeeded);
        // Exactly 3 invocations occurred (1 initial attempt + 2 retries)
        Assert.Equal(3, invocations);
        // Exactly 2 backoff delays occurred (between attempt 1->2 and 2->3)
        Assert.Equal(2, delaysRecorded.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(50), delaysRecorded[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(100), delaysRecorded[1]);
        Assert.Single(finalState.DeadLetters);
        Assert.Equal(1L, finalState.SignalCursor);
    }

    [Fact]
    public async Task Commit_failure_after_handler_success_triggers_retry_and_eventual_success()
    {
        var ct = TestContext.Current.CancellationToken;
        var handlerInvocations = 0;
        var commitInvocations = 0;

        var initial = new TestSubscriberState { SignalCursor = 0 };
        var signal = new Note("commit-retry");

        var (finalState, succeeded) = await ReactionFaultBoundary.ExecuteReactionAsync(
            initial,
            1,
            "b1/chat/p",
            signal,
            (s, sig, token) =>
            {
                handlerInvocations++;
                return Task.FromResult(s with { ProcessedNotes = [.. s.ProcessedNotes, "handled"] });
            },
            (s, token) =>
            {
                commitInvocations++;
                if (commitInvocations < 2)
                {
                    throw new TimeoutException("Transient commit timeout");
                }
                return Task.CompletedTask;
            },
            time: TimeProvider.System,
            maxRetries: 3,
            backoff: attempt => TimeSpan.Zero,
            ct: ct);

        Assert.True(succeeded);
        // Handler was re-invoked on retry
        Assert.Equal(2, handlerInvocations);
        Assert.Equal(2, commitInvocations);
        Assert.Equal(1L, finalState.SignalCursor);
        Assert.Empty(finalState.DeadLetters);
    }

    #endregion
}
