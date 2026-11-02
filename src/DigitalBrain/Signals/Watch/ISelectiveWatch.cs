namespace DigitalBrain.Signals.Watch;

// A watch that tells the runtime which signals to deliver. Callers that only have INeuronProxies
// keep their own filter; a selective watch never starts a reaction for a signal it will not see.
public interface ISelectiveWatch
{
    // A neuron's [Signal] records that match selector; neuron is a reference from INeuronProxies.Get.
    IAsyncEnumerable<T> On<T>(object neuron, string selector, Func<Task> onSubscribed, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal;
}
