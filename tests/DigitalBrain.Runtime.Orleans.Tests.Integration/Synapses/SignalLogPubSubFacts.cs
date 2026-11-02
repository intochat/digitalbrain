using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain;
using DigitalBrain.Runtime.Orleans.Log;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class SignalLogPubSubFacts
{
    private sealed record TestSubscriberState : NeuronState
    {
        public string Content { get; init; } = "";
    }

    [Fact]
    public async Task Save_returns_unified_monotonic_sequence_N()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var neuronA = GrainId.Create("neuron.host", "b1/neuron.a/1");
        var neuronB = GrainId.Create("neuron.host", "b1/neuron.b/2");
        var neuronC = GrainId.Create("neuron.host", "b1/neuron.c/3");

        var stateA = new TestGrainState<TestSubscriberState> { State = new TestSubscriberState { SignalCursor = 0, Content = "A1" } };
        var stateB = new TestGrainState<TestSubscriberState> { State = new TestSubscriberState { SignalCursor = 0, Content = "B1" } };
        var stateC = new TestGrainState<TestSubscriberState> { State = new TestSubscriberState { SignalCursor = 0, Content = "C1" } };

        // Save state and signals across distinct neurons in the brain
        var pos1 = await store.Storage.WriteStateAsync("state", neuronA, stateA, "b1/neuron.a/1", [new Note("sigA1")], null);
        var pos2 = await store.Storage.WriteStateAsync("state", neuronB, stateB, "b1/neuron.b/2", [new Note("sigB1"), new Note("sigB2")], null);
        var pos3 = await store.Storage.AppendSignalsAsync("state", neuronC, "b1/neuron.c/3", [new Note("sigC1")]);
        var pos4 = await store.Storage.WriteStateAsync("state", neuronA, stateA, "b1/neuron.a/1", [], null);

        // All returned positions must be monotonically increasing brain sequence numbers
        Assert.Equal(1L, pos1);
        Assert.Equal(3L, pos2); // 2 signals appended advances position to 3
        Assert.Equal(4L, pos3);
        Assert.Equal(4L, pos4);

        var log = await store.Log("b1");
        Assert.Equal(4L, log.LastPosition);
    }

    private sealed class SlowSignalSubscriber(TimeSpan delay) : ISignalSubscriber
    {
        public async Task Notify(long position)
        {
            await Task.Delay(delay);
        }
    }

    private sealed class StubGrainFactory(ISignalSubscriber subscriber) : IGrainFactory
    {
        public TGrainInterface GetGrain<TGrainInterface>(string? primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey
            => (TGrainInterface)subscriber;

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey => throw new NotImplementedException();
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey => throw new NotImplementedException();
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotImplementedException();
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotImplementedException();
        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => throw new NotImplementedException();
        public IAddressable GetGrain(GrainId grainId) => throw new NotImplementedException();
        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotImplementedException();
        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotImplementedException();
        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string? keyExt) => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string? keyExt) => throw new NotImplementedException();
        public IAddressable GetGrain(Type grainInterfaceType, IdSpan grainPrimaryKey, string? keyExt) => throw new NotImplementedException();
        public IAddressable GetGrain(Type grainInterfaceType, IdSpan grainPrimaryKey) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Publisher_OneWay_notification_completes_in_constant_time()
    {
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        // A subscriber whose notification handler takes 5 seconds (simulating heavy backlog or stall)
        var slowSubscriber = new SlowSignalSubscriber(TimeSpan.FromSeconds(5));
        var factory = new StubGrainFactory(slowSubscriber);
        var signalLog = new BrainSignalLog("b1", store.Logs, grains: factory);

        var sw = Stopwatch.StartNew();
        await signalLog.NotifySubscribersAsync(["sub-1", "sub-2", "sub-3"], 100);
        sw.Stop();

        // OneWay fire-and-forget notification completes immediately in constant time
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500), $"NotifySubscribersAsync took {sw.ElapsedMilliseconds} ms, expected < 500 ms");
    }

    [Fact]
    public async Task Subscriber_commits_domain_state_and_cursor_atomically()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var subscriberGrainId = GrainId.Create("neuron.host", "b1/test.subscriber/sub1");
        var domainState = new TestSubscriberState { SignalCursor = 42, Content = "Domain content with embedded cursor" };
        var grainState = new TestGrainState<TestSubscriberState> { State = domainState };

        // Save domain state + cursor without legacy ReactionCursor side-write
        var pos = await store.Storage.WriteStateAsync("state", subscriberGrainId, grainState, "b1/test.subscriber/sub1", [], null);

        var log = await store.Log("b1");
        // Verify domain state entry was written and can be read back
        var readState = new TestGrainState<TestSubscriberState>();
        await store.Storage.ReadStateAsync("state", subscriberGrainId, readState);
        Assert.NotNull(readState.State);
        Assert.Equal(42, readState.State.SignalCursor);
        Assert.Equal("Domain content with embedded cursor", readState.State.Content);

        // Verify that NO legacy "synapses" side-write was created
        var synapseState = await log.Read(subscriberGrainId.ToString(), "synapses");
        Assert.Null(synapseState);
    }

    [Fact]
    public void Log_compaction_respects_active_subscriber_cursors()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        var subscribers = new List<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)>
        {
            ("active-sub-1", 100, manualTime.Now.AddDays(-1)),
            ("active-sub-2", 150, manualTime.Now.AddHours(-2)),
            ("active-sub-3", 120, manualTime.Now.AddDays(-3))
        };

        const long logLatestPosition = 250;
        var compactionFloor = signalLog.ComputeCompactionFloor(logLatestPosition, subscribers, manualTime.Now);

        // Compaction floor must be min of active cursors (100) to protect unread signals
        Assert.Equal(100, compactionFloor);
    }

    [Fact]
    public void Log_compaction_quarantines_stale_subscribers_after_7_days()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        var subscribers = new List<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)>
        {
            // Stale subscriber: inactive for 8 days, cursor stalled at 50
            ("stale-sub-abandoned", 50, manualTime.Now.AddDays(-8)),
            // Active subscribers: active within last 7 days
            ("active-sub-1", 150, manualTime.Now.AddHours(-1)),
            ("active-sub-2", 200, manualTime.Now.AddDays(-2))
        };

        Assert.True(signalLog.IsQuarantined(subscribers[0].LastActiveAt, manualTime.Now));
        Assert.False(signalLog.IsQuarantined(subscribers[1].LastActiveAt, manualTime.Now));
        Assert.False(signalLog.IsQuarantined(subscribers[2].LastActiveAt, manualTime.Now));

        const long logLatestPosition = 300;
        var compactionFloor = signalLog.ComputeCompactionFloor(logLatestPosition, subscribers, manualTime.Now);

        // Stale subscriber at cursor 50 is quarantined, preventing compaction deadlock.
        // Compaction floor advances to 150 (the min of active subscribers).
        Assert.Equal(150, compactionFloor);
    }
}
