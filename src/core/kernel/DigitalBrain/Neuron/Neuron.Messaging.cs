using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    protected Task SendAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return FireAsync(synapse, [receiver]);
    }

    protected async Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var synapseType = synapse.GetType().FullName!;
        var correlation = _handling?.CorrelationId
            ?? _clientEntryCorrelation
            ?? CorrelationId.New();
        var catalog = ServiceProvider.GetRequiredService<BroadcastCatalog>();

        var receivers = catalog.HandlerGrainTypes(synapseType)
            .Select(grainType => NeuronId.BroadcastReceiver(grainType, Id.Owner, correlation))
            .ToArray();

        await FireAsync(synapse, receivers, correlation);
    }

    private Task<SynapseDelivery> FireAsync(Synapse synapse, NeuronId[] receivers, CorrelationId? correlation = null)
        => FireAsync(synapse, receivers, _handling, correlation);

    private async Task<SynapseDelivery> FireAsync(
        Synapse synapse,
        NeuronId[] receivers,
        SynapseDelivery? causation,
        CorrelationId? correlation = null)
    {
        var sequence = _outgoing.NextSequence
            + (_handling is null ? 0 : _firedWhileHandling.Count);
        var delivery = SynapseDelivery.Create(Snapshot(synapse), Id, sequence, causation, TimeProvider, correlation);

        if (_handling is null)
        {
            _outgoing.Append(delivery);
        }
        else
        {
            _firedWhileHandling.Add(delivery);
        }

        if (receivers.Length > 0)
        {
            _outbox.Add(_entries.SerializeToArray(new OutboxEntry(delivery, receivers, _handlingDepth + 1, Attempts: 0)));
        }

        if (_handling is null)
        {
            await CommitAsync(CancellationToken.None);
            await NotifyWatchersAsync();
            ScheduleDrain();
        }

        return delivery;
    }
}
