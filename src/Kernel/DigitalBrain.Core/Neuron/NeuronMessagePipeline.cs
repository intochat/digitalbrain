using System.Collections.Concurrent;
using System.Reflection;
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

        // Catalog is empty unless a synapse type opts in with [Broadcast]. Product
        // delivery is the synapse graph (plus directed Send). Ghost receivers are
        // correlation-addressed and cannot be named instances — trap 8 / Wave 1.
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

        var graph = PrincipalGraph.Resolve(neuron.Id.Owner);
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

    // v1: journal into emitter incoming (Fire/tool pollers).
    // R8 v2 also dual-addresses via StageOutcomeAddresses (Caller + principal inbox).
    internal SynapseDelivery StageIncomingOutcome(Synapse outcome, SynapseDelivery cause)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(cause);

        var delivery = SynapseDelivery.Create(
            turn.Snapshot(outcome),
            neuron.Id,
            turn.NextOutgoingSequence,
            cause,
            neuron.NeuronTimeProvider,
            principal: VerifiedActor.Current?.PrincipalId ?? cause.Principal);

        journal.AppendIncoming(delivery);
        return delivery;
    }


    // R8 v2: directed send to Caller (if not self) + principal/owner inbox.
    // Stages outbox entries only — never Commit/Fire from drain (nested commit hazard).
    internal void StageOutcomeAddresses(Synapse outcome, SynapseDelivery cause)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(cause);

        if (!IsOutcome(outcome))
        {
            return;
        }

        var receivers = new List<NeuronId>(2);
        if (cause.Caller != neuron.Id)
        {
            receivers.Add(cause.Caller);
        }

        var inbox = cause.Principal is { } principal
            ? IInbox.ForPrincipal(neuron.Id.Owner, principal)
            : IInbox.ForOwner(neuron.Id.Owner);
        if (inbox != neuron.Id && !receivers.Contains(inbox))
        {
            receivers.Add(inbox);
        }

        if (receivers.Count == 0)
        {
            return;
        }

        var delivery = SynapseDelivery.Create(
            turn.Snapshot(outcome),
            neuron.Id,
            turn.NextOutgoingSequence,
            cause,
            neuron.NeuronTimeProvider,
            principal: cause.Principal ?? VerifiedActor.Current?.PrincipalId);

        turn.StageOutgoing(delivery);
        outbox.Add(new OutboxEntry(
            delivery,
            [.. receivers],
            Math.Max(1, turn.CurrentDepth + 1),
            Attempts: 0));
    }

    internal static bool IsOutcome(Synapse synapse)
        => synapse is RouteOutcome or Unrouted;

    private static readonly ConcurrentDictionary<Type, bool> ProjectionFacts = new();

    private static bool IsJournalProjection(Type synapseType)
        => ProjectionFacts.GetOrAdd(
            synapseType,
            static type => type.GetCustomAttribute<JournalProjectionAttribute>() is not null);

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
            correlation,
            principal: VerifiedActor.Current?.PrincipalId ?? causation?.Principal);

        turn.StageOutgoing(delivery);

        if (receivers.Length > 0)
        {
            outbox.Add(new OutboxEntry(
                delivery,
                receivers,
                turn.CurrentDepth + 1,
                Attempts: 0));
        }
        else if (!IsOutcome(synapse)
            && !IsJournalProjection(synapse.GetType())
            && SynapseAlias.Of(synapse.GetType()) is { } unroutedAlias)
        {
            var unrouted = new Unrouted(
                delivery.SynapseId, unroutedAlias, neuron.Id, delivery.CorrelationId);
            StageIncomingOutcome(unrouted, delivery);
            StageOutcomeAddresses(unrouted, delivery);
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
