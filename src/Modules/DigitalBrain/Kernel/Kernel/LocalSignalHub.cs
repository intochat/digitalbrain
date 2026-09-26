using System.Collections.Concurrent;
using DigitalBrain.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Core;

internal sealed class LocalSignalHub : ILocalSignalHub
{
    private readonly ConcurrentDictionary<GrainId, List<INeuronObserver>> _subscribers = new();

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
        if (!_subscribers.TryGetValue(source, out var list)) { return; }
        INeuronObserver[] snapshot;
        lock (list) { snapshot = [.. list]; }
        foreach (var observer in snapshot)
        {
            _ = observer.OnSignalAsync(signal);
        }
    }
}
