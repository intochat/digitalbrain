using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

internal sealed class NeuronMessagePipeline(
    Neuron neuron,
    NeuronJournal journal,
    NeuronOutbox outbox,
    NeuronTurnCoordinator turn,
    NeuronStreamRegistry streams)
{
    internal Task<SynapseDelivery> SendAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        return FireAsync(synapse, [receiver], turn.Handling);
    }

    internal Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        return EmitAsync(synapse, ResolveEmissionCorrelation());
    }

    internal CorrelationId ResolveEmissionCorrelation()
        => turn.Handling?.CorrelationId
            ?? streams.AmbientClientCorrelation
            ?? CorrelationId.New();

    internal async Task EmitAsync(Synapse synapse, CorrelationId correlation)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var synapseType = synapse.GetType().FullName!;
        var catalog = neuron.NeuronServices.GetRequiredService<BroadcastCatalog>();

        var receivers = catalog.HandlerGrainTypes(synapseType)
            .Select(grainType => NeuronId.BroadcastReceiver(
                grainType,
                neuron.Id.Owner,
                correlation))
            .Concat(await ConnectedReceiversAsync(synapse)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext))
            .ToArray();

        await FireAsync(synapse, receivers, turn.Handling, correlation)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task EmitAtDepthAsync(
        Synapse synapse,
        CorrelationId correlation,
        int deliveryDepth)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        ArgumentOutOfRangeException.ThrowIfLessThan(deliveryDepth, 1);

        var restored = turn.CurrentDepth;
        turn.CurrentDepth = deliveryDepth - 1;
        try
        {
            await EmitAsync(synapse, correlation)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        finally
        {
            turn.CurrentDepth = restored;
        }
    }

    internal Task ReplyAsync(Synapse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        var handling = turn.Handling
            ?? throw new InvalidOperationException(
                "ReplyAsync requires an active delivery context. Reply only from a "
                + "HandleAsync turn.");

        return FireAsync(
            response,
            [handling.Caller],
            handling,
            handling.CorrelationId);
    }

    private async Task<IReadOnlyCollection<NeuronId>> ConnectedReceiversAsync(Synapse synapse)
    {
        if (SynapseAlias.Of(synapse.GetType()) is not { } alias)
        {
            return [];
        }

        var graph = ISynapseGraph.ForOwner(neuron.Id.Owner);
        if (graph == neuron.Id)
        {
            return [];
        }

        using var bound = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        try
        {
            var connections = await neuron.NeuronGrainFactory
                .GetGrain<ISynapseGraph>(graph.ToGrainId())
                .ConnectionsFrom(neuron.Id, alias)
                .WaitAsync(bound.Token)
                .ConfigureAwait(true);

            return
            [
                .. connections.Select(connection => connection.Transform is null
                    ? connection.Target
                    : ConnectionRelay.ForConnection(neuron.Id.Owner, connection.ConnectionId)),
            ];
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Synapse graph connection lookup for '{alias}' did not answer within "
                + $"{DeliveryPolicy.ConnectionLookupTimeout}.");
        }
    }

    // An outcome is journaled into this neuron's incoming feed, never delivered: the reader
    // that fired the failed synapse is already polling that feed, and a delivered outcome
    // could itself fail and produce another one.
    internal SynapseDelivery StageIncomingOutcome(Synapse outcome, SynapseDelivery cause)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(cause);

        var delivery = SynapseDelivery.Create(
            turn.Snapshot(outcome),
            neuron.Id,
            turn.NextOutgoingSequence,
            cause,
            neuron.NeuronTimeProvider);

        journal.AppendIncoming(delivery);
        return delivery;
    }

    internal static bool IsOutcome(Synapse synapse)
        => synapse is RouteOutcome or Unrouted;

    private async Task<SynapseDelivery> FireAsync(
        Synapse synapse,
        NeuronId[] receivers,
        SynapseDelivery? causation,
        CorrelationId? correlation = null)
    {
        var delivery = SynapseDelivery.Create(
            turn.Snapshot(synapse),
            neuron.Id,
            turn.NextOutgoingSequence,
            causation,
            neuron.NeuronTimeProvider,
            correlation);

        turn.StageOutgoing(delivery);

        if (receivers.Length > 0)
        {
            outbox.Add(new OutboxEntry(
                delivery,
                receivers,
                turn.CurrentDepth + 1,
                Attempts: 0));
        }
        else if (!IsOutcome(synapse) && SynapseAlias.Of(synapse.GetType()) is { } unroutedAlias)
        {
            StageIncomingOutcome(
                new Unrouted(delivery.SynapseId, unroutedAlias, neuron.Id, delivery.CorrelationId),
                delivery);
        }

        if (turn.Handling is null)
        {
            await outbox.CommitAsync(CancellationToken.None)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await journal.NotifyWatchersAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            outbox.ScheduleDrain();
        }

        return delivery;
    }
}
