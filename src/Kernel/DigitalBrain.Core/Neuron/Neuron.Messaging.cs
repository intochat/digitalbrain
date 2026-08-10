using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

public abstract partial class Neuron
{
    protected Task<SynapseDelivery> SendAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return FireAsync(synapse, [receiver]);
    }

    protected Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return EmitAsync(synapse, ResolveEmissionCorrelation());
    }

    protected CorrelationId ResolveEmissionCorrelation()
        => _handling?.CorrelationId
            ?? AmbientClientEntryCorrelation
            ?? CorrelationId.New();

    protected async Task EmitAsync(Synapse synapse, CorrelationId correlation)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var synapseType = synapse.GetType().FullName!;
        var catalog = ServiceProvider.GetRequiredService<BroadcastCatalog>();

        var receivers = catalog.HandlerGrainTypes(synapseType)
            .Select(grainType => NeuronId.BroadcastReceiver(grainType, Id.Owner, correlation))
            .Concat(await ConnectedReceiversAsync(synapse).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext))
            .ToArray();

        await FireAsync(synapse, receivers, correlation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task<IReadOnlyCollection<NeuronId>> ConnectedReceiversAsync(Synapse synapse)
    {
        if (SynapseAlias.Of(synapse.GetType()) is not { } alias)
        {
            return [];
        }

        var graph = ISynapseGraph.ForOwner(Id.Owner);
        if (graph == Id)
        {
            return [];
        }

        using var bound = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        try
        {
            var connections = await GrainFactory
                .GetGrain<ISynapseGraph>(graph.ToGrainId())
                .ConnectionsFrom(Id, alias)
                .WaitAsync(bound.Token).ConfigureAwait(true);

            return [.. connections.Select(connection => connection.Transform is null
                ? connection.Target
                : ConnectionRelay.ForConnection(Id.Owner, connection.ConnectionId))];
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Synapse graph connection lookup for '{alias}' did not answer within {DeliveryPolicy.ConnectionLookupTimeout}.");
        }
    }

    protected async Task EmitAtDepthAsync(Synapse synapse, CorrelationId correlation, int deliveryDepth)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        ArgumentOutOfRangeException.ThrowIfLessThan(deliveryDepth, 1);

        var restored = CurrentDeliveryDepth;
        CurrentDeliveryDepth = deliveryDepth - 1;
        try
        {
            await EmitAsync(synapse, correlation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        finally
        {
            CurrentDeliveryDepth = restored;
        }
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
            _outbox.Add(_entries.SerializeToArray(new OutboxEntry(delivery, receivers, CurrentDeliveryDepth + 1, Attempts: 0)));
        }

        if (_handling is null)
        {
            await CommitAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await NotifyWatchersAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            ScheduleDrain();
        }

        return delivery;
    }
}
