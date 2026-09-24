using Orleans.Concurrency;

namespace DigitalBrain.Contracts;

// Watching never waits on the neuron's own work: a turn that stops a subscriber, such as an app
// uninstalling its behavior, must not deadlock on that subscriber's final Unwatch.
public interface INeuron : IGrainWithStringKey
{
    [AlwaysInterleave] Task<Guid> Watch(INeuronObserver observer);
    [AlwaysInterleave] Task Unwatch(INeuronObserver observer);
}
