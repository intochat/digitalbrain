using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions;

public static class NeuronReferenceExtensions
{
    // The only public fire: TNeuron must declare IHandle<TSignal>. That is how the assistant
    // and scripts stay type-safe — PublishPost cannot be sent to IBehaviors.
    public static Task<DeliveryOutcome> SendAsync<TNeuron, TSignal>(
        this NeuronReference<TNeuron> neuron,
        TSignal signal,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron, IHandle<TSignal>
        where TSignal : Signal
        => neuron.DeliverAsync(signal, cancellationToken);
}
