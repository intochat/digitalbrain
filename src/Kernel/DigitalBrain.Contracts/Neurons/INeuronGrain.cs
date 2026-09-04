using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

// Orleans delivery and synapse mutation (membrane). Scripts never call this; they Publish /
// Subscribe through IDigitalBrain. SignalSender uses Deliver and is the only journal/synapse
// writer — grain call filters must not Record or Bind (self-send is in-process, not a proxy).
// SubscribeTo asks the source to BindOutgoing.
[Alias("db.v2.neuron-grain")]
public interface INeuronGrain : INeuron
{
    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<DeliveryOutcome> Deliver(
        SignalDelivery delivery,
        CancellationToken cancellationToken = default);

    [Alias(nameof(BindOutgoing))]
    Task BindOutgoing(NeuronId subscriber, string signalType);

    [Alias(nameof(UnbindOutgoing))]
    Task UnbindOutgoing(NeuronId subscriber, string signalType);
}
