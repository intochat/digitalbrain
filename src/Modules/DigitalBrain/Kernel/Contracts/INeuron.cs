using Orleans.Concurrency;

namespace DigitalBrain.Contracts;

public interface INeuron : IGrainWithStringKey
{
    Task<Guid> Watch(INeuronObserver observer);
    // A turn that stops a subscriber, such as an app uninstalling its behavior, must not deadlock on
    // that subscriber's final Unwatch. Removing an observer is safe while signals are being delivered.
    [AlwaysInterleave] Task Unwatch(INeuronObserver observer);
}
