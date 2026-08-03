using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel;

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

    // Outside a delivery turn and a client entry scope every emission would otherwise mint its
    // own correlation, so anything that emits more than once per operation must bind them here.
    protected CorrelationId ResolveEmissionCorrelation()
        => _handling?.CorrelationId
            ?? _clientEntryCorrelation
            ?? CorrelationId.New();

    protected async Task EmitAsync(Synapse synapse, CorrelationId correlation)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var synapseType = synapse.GetType().FullName!;
        var catalog = ServiceProvider.GetRequiredService<BroadcastCatalog>();

        var receivers = catalog.HandlerGrainTypes(synapseType)
            .Select(grainType => NeuronId.BroadcastReceiver(grainType, Id.Owner, correlation))
            .Concat(await SubscribedReceiversAsync(synapse))
            .ToArray();

        await FireAsync(synapse, receivers, correlation);
    }

    private async Task<IReadOnlyCollection<NeuronId>> SubscribedReceiversAsync(Synapse synapse)
    {
        var subscribers = ServiceProvider.GetService<IBroadcastSubscribers>();
        if (subscribers is null || SynapseAlias.Of(synapse.GetType()) is not { } alias)
        {
            return [];
        }

        using var bound = new CancellationTokenSource(DeliveryPolicy.SubscriptionRegistryTimeout);
        try
        {
            return await subscribers.ReceiversFor(Id.Owner, alias, bound.Token);
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            // Failing closed would report "no subscribers", which is indistinguishable from a
            // correct empty result and leaves every subscriber silently deaf. Failing loudly
            // retracts the emitting turn and leaves the outbox to retry it.
            throw new TimeoutException(
                $"Broadcast subscriber lookup for '{alias}' did not answer within {DeliveryPolicy.SubscriptionRegistryTimeout}.");
        }
    }

    // Emissions made outside a delivery turn start at depth zero, which is how a chain of them
    // restarts a budget the kernel believes it is counting. Stamping the depth explicitly lets a
    // caller that knows what caused the emission keep the count running across that gap.
    //
    // Borrowing _handlingDepth for the call rests on two Orleans scheduling properties. First,
    // ReadJournal is the only [AlwaysInterleave] entry point a neuron has and it neither reads nor
    // writes the depth, so the one call that can run inside another turn cannot observe the borrow.
    // Second, RegisterGrainTimer drains take ordinary non-reentrant turns, so a drain cannot start
    // while this one holds the grain. If either stops holding, the repair is to thread the depth
    // through FireAsync rather than to borrow the field.
    protected async Task EmitAtDepthAsync(Synapse synapse, CorrelationId correlation, int deliveryDepth)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        // FireAsync stamps _handlingDepth + 1, so the name is only true when the borrow is one less
        // than the depth asked for. A spoken fact is a delivery, and the shallowest delivery is one.
        ArgumentOutOfRangeException.ThrowIfLessThan(deliveryDepth, 1);

        var restored = _handlingDepth;
        _handlingDepth = deliveryDepth - 1;
        try
        {
            await EmitAsync(synapse, correlation);
        }
        finally
        {
            _handlingDepth = restored;
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
