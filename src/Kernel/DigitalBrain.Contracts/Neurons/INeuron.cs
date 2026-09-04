using Orleans.Concurrency;

using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

// Graph endpoint: accepts a Signal delivery and reports whether a handler ran.
[Alias("db.v2.neuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<DeliveryOutcome> Deliver(
        SignalDelivery delivery,
        CancellationToken cancellationToken = default);
}
