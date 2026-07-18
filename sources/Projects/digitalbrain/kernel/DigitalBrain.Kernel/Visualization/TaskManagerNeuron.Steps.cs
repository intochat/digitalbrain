using System.Text.Json;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Visualization;
using DigitalBrain.Kernel.Visualization;

namespace DigitalBrain.Kernel.Tests.Visualization;

// xUnit-style fast tests for TaskManagerNeuron's projection logic. Mirrors
// the testable-implementation pattern used by CreatorNeuronTests / UserNeuronTests:
// no DurableGrain, no Orleans silo. A TestableTaskManager wraps the same
// TaskManagerProjection helpers the production grain uses, plus a capture
// broadcaster, so behaviour parity is real.
public sealed class TaskManagerNeuronTests
{
    static readonly DateTimeOffset T0 = new(2026, 5, 20, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Observe_then_Tick_broadcasts_TaskManagerCard_with_one_active_row()
    {
        var clock = new ManualClock(T0);
        var broadcaster = new CapturingBroadcaster();
        var manager = new TestableTaskManager(clock, broadcaster);

        var correlationId = Guid.NewGuid();
        await manager.Observe(MakeSynapse(correlationId, "kernel/creator", "data/file-read", T0));
        await manager.Tick();

        var card = broadcaster.Broadcasts.Should().ContainSingle().Subject;
        card.RootWidget.Should().Be("TaskManagerCard");
        card.LibraryName.Should().Be("digitalbrain");
        card.CallerNeuronType.Should().Be(TaskManagerNeuron.TaskManagerNeuronType);

        var payload = DeserializePayload(card);
        payload.Tasks.Should().ContainSingle();
        payload.Totals.Active.Should().Be(1);
        var row = payload.Tasks[0];
        row.CorrelationId.Should().Be(correlationId.ToString());
        row.OriginNeuron.Should().Be("kernel/creator");
        row.EdgeCount.Should().Be(1);
        row.Participating.Should().ContainSingle().Which.Should().Be("data/file-read");
        row.Status.Should().Be("running");
    }

    [Fact]
    public async Task Idle_correlation_ages_out_after_IdleTimeout()
    {
        var clock = new ManualClock(T0);
        var broadcaster = new CapturingBroadcaster();
        var manager = new TestableTaskManager(clock, broadcaster);

        var correlationId = Guid.NewGuid();
        await manager.Observe(MakeSynapse(correlationId, "kernel/creator", "data/file-read", T0));
        await manager.Tick();

        // Advance past IdleTimeout (default 8s) and tick again.
        clock.UtcNow = T0.AddSeconds(9);
        await manager.Tick();

        broadcaster.Broadcasts.Should().HaveCount(2);
        var lastPayload = DeserializePayload(broadcaster.Broadcasts[1]);
        lastPayload.Tasks.Should().BeEmpty();
        lastPayload.Totals.Active.Should().Be(0);
        lastPayload.Totals.Completed.Should().Be(1);
    }

    [Fact]
    public async Task LRU_evicts_oldest_when_max_tracked_reached()
    {
        var clock = new ManualClock(T0);
        var broadcaster = new CapturingBroadcaster();
        var manager = new TestableTaskManager(clock, broadcaster, new TaskManagerOptions
        {
            MaxTracked = 3,
            IdleTimeout = TimeSpan.FromMinutes(10),
        });

        var oldestId = Guid.NewGuid();
        var middleId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        await manager.Observe(MakeSynapse(oldestId, "kernel/creator", "data/a", T0));
        await manager.Observe(MakeSynapse(middleId, "kernel/creator", "data/b", T0.AddMilliseconds(10)));
        await manager.Observe(MakeSynapse(newerId, "kernel/creator", "data/c", T0.AddMilliseconds(20)));

        var freshId = Guid.NewGuid();
        clock.UtcNow = T0.AddMilliseconds(100);
        await manager.Observe(MakeSynapse(freshId, "kernel/creator", "data/d", clock.UtcNow));
        await manager.Tick();

        var payload = DeserializePayload(broadcaster.Broadcasts.Single());
        var ids = payload.Tasks.Select(row => row.CorrelationId).ToArray();
        ids.Should().NotContain(oldestId.ToString());
        ids.Should().Contain(freshId.ToString());
        payload.Totals.Active.Should().Be(3);
        payload.Totals.Completed.Should().Be(1, "eviction counts as completion");
    }

    [Fact]
    public async Task Tick_with_no_delta_skips_broadcast()
    {
        var clock = new ManualClock(T0);
        var broadcaster = new CapturingBroadcaster();
        var manager = new TestableTaskManager(clock, broadcaster);

        var correlationId = Guid.NewGuid();
        await manager.Observe(MakeSynapse(correlationId, "kernel/creator", "data/file-read", T0));
        await manager.Tick();
        // Re-tick without any new Observe: activity-derived signature is unchanged.
        await manager.Tick();

        broadcaster.Broadcasts.Should().ContainSingle("a duplicate projection must not re-broadcast");
    }

    [Fact]
    public async Task Tick_with_clock_advancing_but_no_activity_still_skips_broadcast()
    {
        var clock = new ManualClock(T0);
        var broadcaster = new CapturingBroadcaster();
        var manager = new TestableTaskManager(clock, broadcaster);

        var correlationId = Guid.NewGuid();
        await manager.Observe(MakeSynapse(correlationId, "kernel/creator", "data/file-read", T0));
        await manager.Tick();
        broadcaster.Broadcasts.Should().ContainSingle("first tick after observe must broadcast");

        clock.UtcNow = T0.AddSeconds(1);
        await manager.Tick();
        broadcaster.Broadcasts.Should().ContainSingle("clock advanced but no new activity — must not re-broadcast");

        clock.UtcNow = T0.AddSeconds(2);
        await manager.Tick();
        broadcaster.Broadcasts.Should().ContainSingle("clock advanced again, still no activity — still no extra broadcast");
    }

    [Fact]
    public async Task CancelCorrelation_marks_row_status_cancelling()
    {
        var clock = new ManualClock(T0);
        var broadcaster = new CapturingBroadcaster();
        var manager = new TestableTaskManager(clock, broadcaster);

        var correlationId = Guid.NewGuid();
        await manager.Observe(MakeSynapse(correlationId, "kernel/creator", "data/file-read", T0));
        await manager.HandleCancel(new CancelCorrelation(TargetCorrelationId:   correlationId) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.NewGuid(),
            causationId: null,
            callerNeuronId: Guid.Empty,
            callerNeuronType: "kernel/user",
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: TaskManagerNeuron.TaskManagerNeuronType,
            timestamp: clock.UtcNow
        ) });
        await manager.Tick();

        var payload = DeserializePayload(broadcaster.Broadcasts.Single());
        payload.Tasks.Should().ContainSingle()
            .Which.Status.Should().Be("cancelling");
    }

    static TestSynapse MakeSynapse(Guid correlationId, string callerType, string receiverType, DateTimeOffset at) =>
        new() { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: correlationId,
            causationId: null,
            callerNeuronId: Guid.NewGuid(),
            callerNeuronType: callerType,
            receiverNeuronId: Guid.NewGuid(),
            receiverNeuronType: receiverType,
            timestamp: at
        ) };

    static TaskManagerCardPayload DeserializePayload(RfwCard card) =>
        JsonSerializer.Deserialize<TaskManagerCardPayload>(card.DataJson)!;

    sealed record TestSynapse : Synapse;

    sealed class ManualClock(DateTimeOffset start)
    {
        public DateTimeOffset UtcNow { get; set; } = start;
    }

    sealed class CapturingBroadcaster : ITaskManagerBroadcaster
    {
        public List<RfwCard> Broadcasts { get; } = [];

        public Task BroadcastAsync(RfwCard card, CancellationToken cancellationToken = default)
        {
            Broadcasts.Add(card);
            return Task.CompletedTask;
        }
    }

    // Re-implements the grain loop against the same TaskManagerProjection helpers
    // the production neuron uses. No DurableGrain / Orleans wiring runs.
    sealed class TestableTaskManager
    {
        readonly ManualClock _clock;
        readonly CapturingBroadcaster _broadcaster;
        readonly TaskManagerOptions _options;
        readonly Dictionary<Guid, ActiveTask> _active = new();
        readonly LinkedList<Guid> _lru = new();
        string? _lastSignature;
        int _completed;
        const int FailedPlaceholder = 0;

        public TestableTaskManager(
            ManualClock clock,
            CapturingBroadcaster broadcaster,
            TaskManagerOptions? options = null)
        {
            _clock = clock;
            _broadcaster = broadcaster;
            _options = options ?? new TaskManagerOptions();
        }

        public Task Observe(Synapse synapse)
        {
            TaskManagerProjection.Observe(_active, _lru, _options.MaxTracked, synapse,
                evictedCallback: _ => _completed++);
            return Task.CompletedTask;
        }

        public Task HandleCancel(CancelCorrelation cancel)
        {
            if (_active.TryGetValue(cancel.TargetCorrelationId, out var task))
                task.Status = "cancelling";
            return Task.CompletedTask;
        }

        public async Task Tick()
        {
            var now = _clock.UtcNow;
            TaskManagerProjection.Sweep(_active, _lru, _options.IdleTimeout, now,
                agedOutCallback: _ => _completed++);
            var payload = TaskManagerProjection.Project(_active.Values, _completed, FailedPlaceholder, now);
            var signature = TaskManagerProjection.Signature(payload);
            if (signature == _lastSignature) return;
            _lastSignature = signature;
            var json = JsonSerializer.Serialize(payload);
            await _broadcaster.BroadcastAsync(new RfwCard(LibraryName:        "digitalbrain",
        RootWidget:         "TaskManagerCard",
        DataJson:           json) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            causationId: null,
            callerNeuronId: Guid.NewGuid(),
            callerNeuronType: TaskManagerNeuron.TaskManagerNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: now
        ) });
        }
    }
}
