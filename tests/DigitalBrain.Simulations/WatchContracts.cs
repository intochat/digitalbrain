using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class WatchContracts
{
    [Fact(DisplayName = "a watcher is pushed a delivery it did not ask for again")]
    public async Task AWatcherReceivesDeliveriesWithoutPolling()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("watchers");

        var observer = new RecordingObserver();
        var reference = SimulationCluster.Grains.CreateObjectReference<IJournalObserver>(observer);

        await simulation.Client.Neuron(nameof(Echo), "watched")
            .WatchAsync(JournalKind.Incoming, afterSequence: 0, reference);

        await simulation.SendAsync("Ping", nameof(Echo), "watched", NoValues);

        var pushed = await observer.WaitForAsync(1);

        Assert.Single(pushed);
        Assert.Equal(nameof(Ping), pushed[0].Synapse.GetType().Name);
    }

    [Fact(DisplayName = "a watcher that reconnects with its cursor receives only what it missed")]
    public async Task AReconnectingWatcherCatchesUpFromItsCursor()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("catch-up");

        var handle = simulation.Client.Neuron(nameof(Echo), "resumed");

        var first = new RecordingObserver();
        var firstReference = SimulationCluster.Grains.CreateObjectReference<IJournalObserver>(first);

        await handle.WatchAsync(JournalKind.Incoming, afterSequence: 0, firstReference);
        await simulation.SendAsync("Ping", nameof(Echo), "resumed", NoValues);

        var seen = await first.WaitForAsync(1);
        var cursor = first.Cursor;

        await handle.UnwatchAsync(firstReference);

        await simulation.SendAsync("Ping", nameof(Echo), "resumed", NoValues);

        var second = new RecordingObserver();
        var secondReference = SimulationCluster.Grains.CreateObjectReference<IJournalObserver>(second);

        await handle.WatchAsync(JournalKind.Incoming, cursor, secondReference);

        var missed = await second.WaitForAsync(1);

        Assert.Single(seen);
        Assert.Single(missed);
        Assert.True(second.Cursor > cursor, "the resumed cursor did not advance past what was already seen");
    }

    [Fact(DisplayName = "a watcher whose cursor has fallen off the log is reset with a snapshot, never a gap")]
    public async Task AStaleCursorIsResetWithASnapshot()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("stale-cursor");

        var observer = new RecordingObserver();
        var reference = SimulationCluster.Grains.CreateObjectReference<IJournalObserver>(observer);

        await simulation.SendAsync("Ping", nameof(Echo), "compacted", NoValues);

        await simulation.Client.Neuron(nameof(Echo), "compacted")
            .WatchAsync(JournalKind.Incoming, afterSequence: 9_000_000, reference);

        var reset = await observer.WaitForResetAsync();

        Assert.NotNull(reset);
        Assert.True(reset.ResumeSequence >= 0, "a reset must carry a resume sequence");
    }

    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    private sealed class RecordingObserver : IJournalObserver
    {
        private readonly ConcurrentQueue<SynapseDelivery> _deliveries = new();
        private readonly TaskCompletionSource<JournalSnapshot> _reset =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public long Cursor { get; private set; }

        public Task ObserveAsync(JournalKind kind, JournalRead read)
        {
            Cursor = read.ResumeSequence;

            foreach (var delivery in read.Delta)
            {
                _deliveries.Enqueue(delivery);
            }

            if (read.ResetSnapshot is { } snapshot)
            {
                _reset.TrySetResult(snapshot);
            }

            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<SynapseDelivery>> WaitForAsync(int count)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);

            while (_deliveries.Count < count && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Yield();
            }

            Assert.True(_deliveries.Count >= count, $"expected {count} pushed deliveries, observed {_deliveries.Count}");

            return [.. _deliveries];
        }

        public async Task<JournalRead> WaitForResetAsync()
        {
            var snapshot = await _reset.Task.WaitAsync(TimeSpan.FromSeconds(20));

            return new JournalRead(Cursor, [], snapshot);
        }
    }
}
