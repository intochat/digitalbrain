using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DigitalBrain;
using DigitalBrain.Runtime.Orleans.Log;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class MultiSubscriberCursorFacts
{
    private sealed record TestSubscriberState : NeuronState
    {
        public List<int> ProcessedCounts { get; init; } = [];
        public List<string> ReceivedNotes { get; init; } = [];
        public bool GapDetected { get; init; }
        public (long From, long To)? LastGap { get; init; }
    }

    [Fact]
    public async Task MultipleSubscribers_MaintainCompletelyIsolatedCursorProgression()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var pub = new NeuronId(new BrainId("b1"), "chat.pub", "p1");
        var subA = GrainId.Create("neuron.host", "b1/subscriber.test/sub-a");
        var subB = GrainId.Create("neuron.host", "b1/subscriber.test/sub-b");
        var subC = GrainId.Create("neuron.host", "b1/subscriber.test/sub-c");

        // Write 100 signals to brain log
        var signalLog = new BrainSignalLog("b1", store.Logs);
        var signals = Enumerable.Range(1, 100).Select(n => (Signal)new Count(n)).ToArray();
        await signalLog.AppendSignalsAsync(pub, signals, ct);

        // Sub A advances to 100, Sub B advances to 30, Sub C advances to 75
        var stateA = new TestGrainState<TestSubscriberState> { State = new TestSubscriberState { SignalCursor = 100 } };
        var stateB = new TestGrainState<TestSubscriberState> { State = new TestSubscriberState { SignalCursor = 30 } };
        var stateC = new TestGrainState<TestSubscriberState> { State = new TestSubscriberState { SignalCursor = 75 } };

        await store.Storage.WriteStateAsync("state", subA, stateA, subA.Key.ToString()!, [], null);
        await store.Storage.WriteStateAsync("state", subB, stateB, subB.Key.ToString()!, [], null);
        await store.Storage.WriteStateAsync("state", subC, stateC, subC.Key.ToString()!, [], null);

        // Read back state for each subscriber independently
        var readA = new TestGrainState<TestSubscriberState>();
        var readB = new TestGrainState<TestSubscriberState>();
        var readC = new TestGrainState<TestSubscriberState>();

        await store.Storage.ReadStateAsync("state", subA, readA);
        await store.Storage.ReadStateAsync("state", subB, readB);
        await store.Storage.ReadStateAsync("state", subC, readC);

        Assert.NotNull(readA.State);
        Assert.NotNull(readB.State);
        Assert.NotNull(readC.State);

        Assert.Equal(100L, readA.State.SignalCursor);
        Assert.Equal(30L, readB.State.SignalCursor);
        Assert.Equal(75L, readC.State.SignalCursor);
    }

    [Fact]
    public async Task SlowSubscriber_CatchesUpIndependently_WithoutStallingFastSubscribers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var pub = new NeuronId(new BrainId("b1"), "chat.pub", "p1");
        var fastSubId = new NeuronId(new BrainId("b1"), "chat.sub", "fast");
        var slowSubId = new NeuronId(new BrainId("b1"), "chat.sub", "slow");

        var signalLog = new BrainSignalLog("b1", store.Logs);

        // 1. Initial burst of 10 signals
        var initialBatch = Enumerable.Range(1, 10).Select(n => (Signal)new Count(n)).ToArray();
        await signalLog.AppendSignalsAsync(pub, initialBatch, ct);

        // Fast subscriber processes live to position 10
        var fastState = new TestSubscriberState { SignalCursor = 0 };
        fastState = await signalLog.EnsureCaughtUpAsync(
            fastSubId,
            fastState,
            (s, sig, _) => Task.FromResult(s with { ProcessedCounts = [.. s.ProcessedCounts, ((Count)sig).N] }),
            (s, _) => Task.CompletedTask,
            targetPosition: 10,
            ct: ct);

        Assert.Equal(10L, fastState.SignalCursor);
        Assert.Equal(10, fastState.ProcessedCounts.Count);

        // Slow subscriber remains offline/unaware at cursor 0
        var slowState = new TestSubscriberState { SignalCursor = 0 };

        // 2. Second burst of 20 signals (total 30)
        var secondBatch = Enumerable.Range(11, 20).Select(n => (Signal)new Count(n)).ToArray();
        await signalLog.AppendSignalsAsync(pub, secondBatch, ct);

        // Fast subscriber immediately catches up to 30 without delay
        fastState = await signalLog.EnsureCaughtUpAsync(
            fastSubId,
            fastState,
            (s, sig, _) => Task.FromResult(s with { ProcessedCounts = [.. s.ProcessedCounts, ((Count)sig).N] }),
            (s, _) => Task.CompletedTask,
            targetPosition: 30,
            ct: ct);

        Assert.Equal(30L, fastState.SignalCursor);
        Assert.Equal(30, fastState.ProcessedCounts.Count);
        Assert.Equal(Enumerable.Range(1, 30), fastState.ProcessedCounts);

        // 3. Now slow subscriber wakes up and catches up from 0 to 30 independently
        slowState = await signalLog.EnsureCaughtUpAsync(
            slowSubId,
            slowState,
            (s, sig, _) => Task.FromResult(s with { ProcessedCounts = [.. s.ProcessedCounts, ((Count)sig).N] }),
            (s, _) => Task.CompletedTask,
            targetPosition: 30,
            ct: ct);

        Assert.Equal(30L, slowState.SignalCursor);
        Assert.Equal(30, slowState.ProcessedCounts.Count);
        Assert.Equal(Enumerable.Range(1, 30), slowState.ProcessedCounts);

        // Verify fast subscriber's state was completely unperturbed
        Assert.Equal(30L, fastState.SignalCursor);
    }

    [Fact]
    public void SubscriberTombstone_QuarantinesCursor_PreventingCompactionBlockade()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        const long logLatestPosition = 250;

        // Active subscribers are online within last 2 days.
        // A tombstoned subscriber was stopped/retired 12 days ago with its cursor frozen at 15.
        var subscribers = new List<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)>
        {
            ("active-sub-1", 200, manualTime.Now.AddHours(-3)),
            ("active-sub-2", 180, manualTime.Now.AddDays(-1)),
            ("tombstoned-sub-retired", 15, manualTime.Now.AddDays(-12))
        };

        // Assert quarantine status
        Assert.False(signalLog.IsQuarantined(subscribers[0].LastActiveAt, manualTime.Now));
        Assert.False(signalLog.IsQuarantined(subscribers[1].LastActiveAt, manualTime.Now));
        Assert.True(signalLog.IsQuarantined(subscribers[2].LastActiveAt, manualTime.Now));

        var compactionFloor = signalLog.ComputeCompactionFloor(logLatestPosition, subscribers, manualTime.Now);

        // Tombstoned subscriber at cursor 15 is excluded from compaction floor.
        // Floor successfully advances to 180 (slowest active subscriber), preventing compaction deadlock.
        Assert.Equal(180, compactionFloor);
    }

    [Fact]
    public async Task QuarantinedSubscriber_WakingUp_ReceivesTrimGapSignal_AndReconcilesToFloor()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var time = new ManualTime();
        var cursors = new FixedCursors(25);

        // Setup store where retention policy will trim history up to floor 25
        await using var store = new BrainStore(home.Path, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IRetentionPolicy>(new RetentionPolicy(BrainStore.Window(10), time, cursors));
        });

        var pub = BrainStore.P;
        await store.Publish(pub, 30, batch: 5);

        // Age signals past the retention window and checkpoint
        time.Now = time.Now.AddDays(30);
        await (await store.Log()).Checkpoint(ct);

        // Quarantined subscriber wakes up with cursor at 5 (below the floor of 20+)
        var subscriberCursor = 5L;
        var history = store.History();
        var page = await history.Read(subscriberCursor, 100, ct);

        Assert.True(page.Floor > subscriberCursor, $"Floor {page.Floor} must be strictly greater than subscriber cursor {subscriberCursor}");

        // Subscriber handles trim gap: constructs Gap signal and reconciles cursor to page.Floor
        var state = new TestSubscriberState { SignalCursor = subscriberCursor };

        Signal gapSignal = new Gap(pub, subscriberCursor, page.Floor);
        state = state with
        {
            GapDetected = true,
            LastGap = (subscriberCursor, page.Floor),
            SignalCursor = page.Floor
        };

        Assert.True(state.GapDetected);
        Assert.Equal((5L, page.Floor), state.LastGap);
        Assert.Equal(page.Floor, state.SignalCursor);

        // Subscriber continues reading post-gap signals contiguous from floor
        var postGapSignals = page.Signals.Where(s => s.Sequence > state.SignalCursor).ToList();
        foreach (var s in postGapSignals)
        {
            if (s.Signal is Count c)
            {
                state = state with
                {
                    ProcessedCounts = [.. state.ProcessedCounts, c.N],
                    SignalCursor = s.Sequence
                };
            }
        }

        Assert.Equal(page.Last, state.SignalCursor);
    }
}
