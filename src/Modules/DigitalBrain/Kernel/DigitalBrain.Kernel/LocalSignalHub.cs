using DigitalBrain.Client;
using System.Collections.Concurrent;
using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Runtime;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel;

public sealed class LocalSignalHub(ILogger<LocalSignalHub> logger) : ILocalSignalHub
{
    private readonly ConcurrentDictionary<GrainId, List<INeuronObserver>> _subscribers = new();
    private readonly Lock _gate = new();
    private readonly HashSet<Action<Signal>> _siloSubscribers = [];

    // Live, bounded subscriptions across all sources on this silo. No replay or domain state.
    public Subscription<T> Subscribe<T>() where T : Signal
    {
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1024)
        { SingleReader = true, AllowSynchronousContinuations = false });
        void Receive(Signal signal)
        {
            if (signal is T typed && !channel.Writer.TryWrite(typed))
            { logger.LogWarning("A slow signal observer missed a {SignalType} signal.", signal.GetType().Name); }
        }
        lock (_gate) { _siloSubscribers.Add(Receive); }
        return new(channel.Reader, () =>
        {
            lock (_gate) { _siloSubscribers.Remove(Receive); channel.Writer.TryComplete(); }
        });
    }

    public void Publish(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        lock (_gate) { foreach (var receive in _siloSubscribers) { receive(signal); } }
    }

    public sealed class Subscription<T>(ChannelReader<T> reader, Action close) : IDisposable where T : Signal
    {
        public ChannelReader<T> Reader { get; } = reader;
        public void Dispose() => close();
    }

    public void Subscribe(GrainId source, INeuronObserver observer)
    {
        var list = _subscribers.GetOrAdd(source, _ => []);
        lock (list)
        {
            if (!list.Contains(observer)) { list.Add(observer); }
        }
    }

    public void Unsubscribe(GrainId source, INeuronObserver observer)
    {
        if (!_subscribers.TryGetValue(source, out var list)) { return; }
        lock (list) { list.Remove(observer); }
    }

    public void Publish(GrainId source, Signal signal)
    {
        Publish(signal);
        if (!_subscribers.TryGetValue(source, out var list)) { return; }
        INeuronObserver[] snapshot;
        lock (list) { snapshot = [.. list]; }
        foreach (var observer in snapshot)
        {
            _ = observer.OnSignalAsync(signal);
        }
    }
}
