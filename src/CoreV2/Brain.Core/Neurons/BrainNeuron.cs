using System.Collections.Immutable;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Core.Endpoints;
using Brain.Core.Outbox;

namespace Brain.Core.Neurons;

internal abstract class BrainNeuron<TState>
{
    private readonly EndpointAddress _endpoint;
    private readonly INeuronTurnStore<TState> _store;
    private readonly IGraphRouteResolver _routes;
    private readonly TimeProvider _clock;

    protected BrainNeuron(
        EndpointAddress endpoint,
        INeuronTurnStore<TState> store,
        IGraphRouteResolver routes,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(routes);
        _endpoint = endpoint;
        _store = store;
        _routes = routes;
        _clock = clock ?? TimeProvider.System;
    }

    protected async Task<TResult> ExecuteTurnAsync<TResult>(
        ActivityContext activity,
        Func<NeuronTurn<TState>, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var turn = new NeuronTurn<TState>(_store.Read(), _endpoint, activity, _clock);
        var result = await action(turn).ConfigureAwait(false);
        _store.Commit(turn.Before, turn.Commit());
        return result;
    }

    protected void SendAsync(
        NeuronTurn<TState> turn,
        EndpointAddress target,
        ContractId contract)
    {
        ArgumentNullException.ThrowIfNull(turn);
        turn.StageDirectedMessage(target, contract);
    }

    protected async Task<EmissionOutcome> EmitAsync<TEvent>(
        NeuronTurn<TState> turn,
        TEvent domainEvent,
        ContractId eventContract,
        CancellationToken cancellationToken)
        where TEvent : class, IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(domainEvent);
        var routes = await _routes.ResolveAsync(_endpoint, eventContract, turn.Activity, cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(routes);

        var snapshots = routes.Select(static route => new DeliverySnapshot(
            DeliveryId.New(),
            route.Target,
            route.Synapse,
            route.Revision,
            route.InputContract,
            route.OutputContract,
            route.Reshape)).ToImmutableArray();
        return turn.StageEmission(eventContract, snapshots);
    }
}
