using System.Collections.Concurrent;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

public sealed class StreamWake
{
    private readonly ConcurrentDictionary<NeuronId, TaskCompletionSource> _waiters = new();

    // Call before reading the journal so a delivery between the read and the await is not lost.
    public Task NextAsync(NeuronId neuron)
        => _waiters.GetOrAdd(neuron, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public void Publish(NeuronId neuron)
    {
        if (_waiters.TryRemove(neuron, out var waiter))
        {
            waiter.TrySetResult();
        }
    }
}
