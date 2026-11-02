using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain;
using DigitalBrain.Runtime.Orleans.Log;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class EnsureCaughtUpFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    private sealed record TestSubscriberState : NeuronState
    {
        public List<int> ProcessedValues { get; init; } = [];
    }

    [Fact]
    public async Task Dropped_notification_healed_by_EnsureCaughtUpAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var publisher = NeuronId.Parse("b1/chat/p");
        var subscriber = NeuronId.Parse("b1/chat/sub");

        // Publisher appends 3 signals to brain log
        var log = await store.Log("b1");
        await log.AppendEntry(BrainStore.GrainOf(publisher).ToString(), "state", null, BrainRecordChange.None, [],
            publisher.ToString(), [new Count(1), new Count(2), new Count(3)], ct);

        Assert.Equal(3L, log.LastPosition);

        // Subscriber was offline or notification was dropped; cursor is at 0
        var state = new TestSubscriberState { SignalCursor = 0 };
        TestSubscriberState? committedState = null;

        var signalLog = new BrainSignalLog("b1", store.Logs);

        // Call EnsureCaughtUpAsync up to target position 3
        var finalState = await signalLog.EnsureCaughtUpAsync(
            subscriber,
            state,
            (s, sig, token) =>
            {
                var val = sig switch
                {
                    Count c => c.N,
                    _ => 0
                };
                return Task.FromResult(s with
                {
                    ProcessedValues = [.. s.ProcessedValues, val]
                });
            },
            (s, token) =>
            {
                committedState = s;
                return Task.CompletedTask;
            },
            targetPosition: 3,
            ct: ct);

        Assert.Equal(3, finalState.SignalCursor);
        Assert.Equal([1, 2, 3], finalState.ProcessedValues);
        Assert.NotNull(committedState);
        Assert.Equal(3, committedState.SignalCursor);
    }

    [Fact]
    public async Task OnActivateAsync_is_constant_time()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Source = "b1/probe.counter/perf-activate";
        const string Listener = "b1/probe.listener/perf-activate";

        // Pre-fill the source with signals before listener is activated
        var counter = silo.Get<ICounter>(Source);
        for (var i = 1; i <= 50; i++)
        {
            await counter.Add(1);
        }

        // Measure listener grain activation time
        var sw = Stopwatch.StartNew();
        var listener = silo.Get<IListener>(Listener);
        await listener.Follow(Source);
        sw.Stop();

        // Activation must be constant time O(1) and not scale with unread signal backlog
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Activation took {sw.ElapsedMilliseconds} ms, expected < 2000 ms");
    }

    [Fact]
    public async Task Reconciliation_heartbeat_pulls_unread_signals()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var publisher = NeuronId.Parse("b1/chat/p");
        var subscriber = NeuronId.Parse("b1/chat/sub-reconcile");

        var log = await store.Log("b1");
        // Publisher publishes initial signal
        await log.AppendEntry(BrainStore.GrainOf(publisher).ToString(), "state", null, BrainRecordChange.None, [],
            publisher.ToString(), [new Note("msg-1")], ct);

        var state = new TestSubscriberState { SignalCursor = 1 };

        // Publisher publishes subsequent signals while subscriber is idle
        await log.AppendEntry(BrainStore.GrainOf(publisher).ToString(), "state", null, BrainRecordChange.None, [],
            publisher.ToString(), [new Note("msg-2"), new Note("msg-3")], ct);

        var targetPosition = log.LastPosition;
        Assert.Equal(3L, targetPosition);

        var signalLog = new BrainSignalLog("b1", store.Logs);
        var handledNotes = new List<string>();

        // Background reconciliation heartbeat executes catch-up
        var caughtUp = await signalLog.EnsureCaughtUpAsync(
            subscriber,
            state,
            (s, sig, token) =>
            {
                if (sig is Note n)
                {
                    handledNotes.Add(n.Text);
                }
                return Task.FromResult(s);
            },
            (s, token) => Task.CompletedTask,
            targetPosition: targetPosition,
            ct: ct);

        Assert.Equal(3, caughtUp.SignalCursor);
        Assert.Equal(["msg-2", "msg-3"], handledNotes);
    }
}
