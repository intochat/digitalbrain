using DigitalBrain.Abstractions.Signals;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// The mailbox, not the object. IGmail/IPlaywright stay verb-only.
// Webhooks and scenarios post facts here; Drain is the grain waking itself.
[Alias("db.v3.neuron-inbox")]
public interface INeuronInbox : IGrainWithStringKey
{
    [Alias(nameof(HandleSignal))]
    [AlwaysInterleave]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<SignalAdmission> HandleSignal(SignalDelivery delivery, CancellationToken cancellationToken = default);

    [OneWay, Alias("drain")]
    Task Drain();
}
