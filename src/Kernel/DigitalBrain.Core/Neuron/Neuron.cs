using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

public abstract class Neuron :
    DurableGrain,
    INeuron,
    IOwnerBoundGrain
{
    private delegate Task HandlerInvoker(object neuron, Synapse synapse, CancellationToken cancellationToken);

    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<Type, HandlerInvoker>> HandlersByNeuronType = new();
    private static readonly ConcurrentDictionary<Type, bool> ProjectionFacts = new();

    private readonly NeuronJournal _journal;
    private readonly ConcurrentDictionary<Guid, CorrelationId> _clientStreamCorrelations = new();
    private readonly ConcurrentDictionary<Guid, GrainId?> _enumerationInitiators = new();
    private readonly ConcurrentDictionary<Guid, SynapseDelivery> _streamedCapabilityRequests = new();

    private SynapseDelivery? _handling;
    private CorrelationId? _ambientClientCorrelation;

    protected Neuron()
    {
        TimeProvider =
            ServiceProvider.GetKeyedService<TimeProvider>(NeuronTime.ServiceKey)
            ?? System.TimeProvider.System;

        _journal = new NeuronJournal(this, ServiceProvider);
    }

    public NeuronId Id
        => NeuronId.FromGrainKey(
            this.GetGrainId().Type.ToString()!,
            this.GetPrimaryKeyString());

    protected TimeProvider TimeProvider { get; }

    protected NeuronId? CurrentDeliveryCaller => _handling?.Caller;

    // A handler stamping provenance needs the correlation of the request that asked for it;
    // the unforgeable half of a provenance record can only come from the delivery.
    protected CorrelationId? CurrentDeliveryCorrelation => _handling?.CorrelationId;

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());

        await base.OnActivateAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await OnNeuronActivatedAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    }

    protected virtual Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task Deliver(
        SynapseDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        await DispatchDeliveryAsync(delivery, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
        => Task.FromResult(_journal.Read(kind, afterSequence));

    public async Task Watch(
        JournalKind kind,
        long afterSequence,
        IJournalObserver observer)
        => await _journal.WatchAsync(kind, afterSequence, observer)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public Task Unwatch(IJournalObserver observer)
    {
        _journal.Unwatch(observer);
        return Task.CompletedTask;
    }

    protected async Task<SynapseDelivery> SendAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var delivery = await StageOutgoingAsync(synapse, _handling)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await DeliverToAsync(receiver, delivery)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return delivery;
    }

    protected Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        return EmitAsync(synapse, ResolveEmissionCorrelation());
    }

    protected CorrelationId ResolveEmissionCorrelation()
        => _handling?.CorrelationId
            ?? _ambientClientCorrelation
            ?? CorrelationId.New();

    protected async Task EmitAsync(Synapse synapse, CorrelationId correlation)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var delivery = await StageOutgoingAsync(synapse, _handling, correlation)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _ = delivery;
    }

    protected async Task ReplyAsync(
        Synapse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        var handling = _handling
            ?? throw new InvalidOperationException(
                "ReplyAsync requires an active delivery context. Reply only from a "
                + "HandleAsync turn.");

        var delivery = await StageOutgoingAsync(response, handling, handling.CorrelationId)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        // The caller's turn is awaiting this one, so an awaited call back into the caller
        // would deadlock two serialized grains. The reply rides an unawaited grain call
        // (queued even when the caller is this neuron); a lost reply is telemetry.
        _ = DeliverReplyAsync(handling.Caller, delivery);
    }

    // Refusing an unhandled synapse here is correct in principle but is NOT this slice's work:
    // ReplyAsync addresses the caller, and callers routinely have no IHandle for the reply
    // type, so refusing breaks every request/reply in the product. It belongs to the turn and
    // delivery hardening, with an explicit accept-list for reply sinks.
    protected virtual Task OnUnboundSynapseAsync(
        Synapse synapse,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected new IDisposable RegisterTimer(
        Func<object, Task> callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period)
        => throw new InvalidOperationException(
            $"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require "
            + "serialized turns.");

    internal async Task<SynapseDelivery> StageOutgoingAsync(
        Synapse synapse,
        SynapseDelivery? cause,
        CorrelationId? correlation = null)
    {
        var delivery = SynapseDelivery.Create(
            synapse,
            Id,
            _journal.OutgoingNextSequence,
            cause,
            TimeProvider,
            correlation,
            principal: VerifiedActor.Current?.PrincipalId ?? cause?.Principal);

        _journal.AppendOutgoing(delivery);
        await WriteStateAsync().ConfigureAwait(true);
        await _journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return delivery;
    }

    private async Task DispatchDeliveryAsync(
        SynapseDelivery delivery,
        CancellationToken cancellationToken)
    {
        using var handling = SynapseTelemetry.Source.StartActivity("handle");

        handling?.SetTag(SynapseTelemetry.ReceiverTag, Id.ToString());
        handling?.SetTag(SynapseTelemetry.SynapseTag, delivery.Synapse.GetType().Name);
        handling?.SetTag(SynapseTelemetry.CorrelationTag, delivery.CorrelationId.ToString());

        var previousHandling = _handling;
        _handling = delivery;

        // Re-enter the verified principal that rode the delivery so grants, graph
        // partition, and stamps apply on the receiving turn.
        using var principalScope = VerifiedActor.Enter(
            delivery.Principal is { } principal
                ? new ActorContext(principal, "_delivery")
                : null);

        try
        {
            await DispatchSynapseAsync(delivery.Synapse, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            _journal.AppendIncoming(delivery);
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            await _journal.NotifyWatchersAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception failure)
        {
            handling?.SetStatus(ActivityStatusCode.Error, failure.Message);
            throw;
        }
        finally
        {
            _handling = previousHandling;
        }
    }

    private Task DeliverToAsync(NeuronId receiver, SynapseDelivery delivery)
        => receiver == Id
            // A grain call to self would deadlock the serialized turn; dispatch in place.
            ? DispatchDeliveryAsync(delivery, CancellationToken.None)
            : GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).Deliver(delivery);

    private async Task DeliverReplyAsync(NeuronId receiver, SynapseDelivery delivery)
    {
        try
        {
            await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId())
                .Deliver(delivery)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception undelivered)
        {
            SynapseTelemetry.ReplyDropped(Id, receiver, undelivered);
        }
    }

    // An outcome is journaled into this neuron's incoming feed, never delivered: the reader
    // that fired the failed synapse is already polling that feed, and a delivered outcome
    // could itself fail and produce another one.
    private async Task StageIncomingOutcomeAsync(Synapse outcome, SynapseDelivery cause)
    {
        var delivery = SynapseDelivery.Create(
            outcome,
            Id,
            _journal.OutgoingNextSequence,
            cause,
            TimeProvider,
            principal: VerifiedActor.Current?.PrincipalId ?? cause.Principal);

        _journal.AppendIncoming(delivery);
        await WriteStateAsync().ConfigureAwait(true);
        await _journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal ClientEntryCorrelationScope EnterClientEntryCorrelation(CorrelationId correlation)
    {
        var previous = _ambientClientCorrelation;
        _ambientClientCorrelation = correlation;
        return new ClientEntryCorrelationScope(this, previous);
    }

    internal void RegisterClientStreamCorrelation(Guid enumerationId, CorrelationId correlation)
        => _clientStreamCorrelations[enumerationId] = correlation;

    internal bool TryGetClientStreamCorrelation(
        Guid enumerationId,
        out CorrelationId correlation)
        => _clientStreamCorrelations.TryGetValue(enumerationId, out correlation);

    internal void ForgetClientStreamCorrelation(Guid enumerationId)
        => _clientStreamCorrelations.TryRemove(enumerationId, out _);

    internal void BindStreamedEnumeration(Guid enumerationId, GrainId? initiator)
        => _enumerationInitiators[enumerationId] = initiator;

    internal void RequireStreamedEnumerationInitiator(Guid enumerationId, GrainId? caller)
    {
        if (!_enumerationInitiators.TryGetValue(enumerationId, out var initiator))
        {
            throw new NeuronAuthorizationException(
                $"Enumeration '{enumerationId}' is not bound to an initiator on neuron "
                + $"'{Id}', so '{nameof(IAsyncEnumerableGrainExtension.MoveNext)}' and "
                + $"'{nameof(IAsyncEnumerableGrainExtension.DisposeAsync)}' are refused.");
        }

        if (!GrainIdEquals(initiator, caller))
        {
            throw new NeuronAuthorizationException(
                $"Enumeration '{enumerationId}' on neuron '{Id}' can be continued or "
                + "disposed only by its initiator.");
        }
    }

    internal void ReleaseStreamedEnumeration(Guid enumerationId)
        => _enumerationInitiators.TryRemove(enumerationId, out _);

    internal async Task<SynapseDelivery> BeginCapabilityRequestAsync(
        string contract,
        string method,
        NeuronId target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        return await StageOutgoingAsync(new CapabilityRequested(contract, method, target), _handling)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal bool TryRegisterStreamedCapabilityRequest(
        Guid enumerationId,
        SynapseDelivery request)
        => _streamedCapabilityRequests.TryAdd(enumerationId, request);

    internal bool TryClaimStreamedCapabilityRequest(
        Guid enumerationId,
        out SynapseDelivery request)
        => _streamedCapabilityRequests.TryRemove(enumerationId, out request!);

    internal async Task RecordCapabilityOutcomeAsync(
        CapabilityOutcome outcome,
        SynapseDelivery request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Synapse fact = outcome switch
        {
            CapabilityOutcome.Completed => new CapabilityCompleted(request.SynapseId),
            CapabilityOutcome.Failed => new CapabilityFailed(request.SynapseId),
            CapabilityOutcome.Rejected => new CapabilityRejected(request.SynapseId),
            CapabilityOutcome.Abandoned => new CapabilityAbandoned(request.SynapseId),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

        await StageOutgoingAsync(fact, request)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task RecordStreamedCapabilityRequestAsync(
        SynapseDelivery delivery,
        GrainId? source)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        RequireAuthorizedCapabilityDelivery(delivery, source);

        _journal.AppendIncoming(delivery);
        await WriteStateAsync().ConfigureAwait(true);
        await _journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task<CapabilityTurn> BeginIncomingCapabilityRequestAsync(
        SynapseDelivery delivery,
        GrainId? source)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (_handling is not null)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot begin a capability request while it is already "
                + $"handling '{_handling.SynapseId}'.");
        }

        RequireAuthorizedCapabilityDelivery(delivery, source);

        _journal.AppendIncoming(delivery);
        await WriteStateAsync().ConfigureAwait(true);
        await _journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var turn = new CapabilityTurn(_handling);
        _handling = delivery;
        return turn;
    }

    internal async Task CompleteIncomingCapabilityRequestAsync(CapabilityTurn turn)
    {
        await WriteStateAsync().ConfigureAwait(true);
        await _journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _handling = turn.PreviousHandling;
    }

    internal Task FailIncomingCapabilityRequestAsync(CapabilityTurn turn)
    {
        _handling = turn.PreviousHandling;
        return Task.CompletedTask;
    }

    private void RequireAuthorizedCapabilityDelivery(SynapseDelivery delivery, GrainId? source)
    {
        if (delivery.Synapse is not CapabilityRequested request || request.Target != Id)
        {
            throw new InvalidOperationException(
                $"The capability request delivery does not target neuron '{Id}'.");
        }

        var sourceMatches = source is not null
            && NeuronId.FromGrainKey(
                source.Value.Type.ToString()
                    ?? throw new InvalidOperationException(
                        "The capability caller has no grain type."),
                source.Value.Key.ToString()) == delivery.Caller;

        if (!sourceMatches)
        {
            throw new NeuronAuthorizationException(
                $"The capability request caller '{delivery.Caller}' does not authorize its "
                + "actual Orleans source.");
        }
    }

    private Task DispatchSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
        => HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
            ? handler(this, synapse, cancellationToken)
            : OnUnboundSynapseAsync(synapse, cancellationToken);

    private static IReadOnlyDictionary<Type, HandlerInvoker> HandlersFor(Type neuronType)
        => HandlersByNeuronType.GetOrAdd(neuronType, static type => BuildHandlers(type));

    private static Dictionary<Type, HandlerInvoker> BuildHandlers(Type neuronType)
    {
        var handlers = new Dictionary<Type, HandlerInvoker>();

        foreach (var handled in neuronType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IHandle<>)))
        {
            var synapseType = handled.GetGenericArguments()[0];
            var handleMethod = handled.GetMethod(nameof(IHandle<>.HandleAsync))
                ?? throw new MissingMethodException(handled.FullName, nameof(IHandle<>.HandleAsync));

            handlers[synapseType] = (neuron, synapse, cancellationToken) => (Task)handleMethod.Invoke(
                neuron,
                BindingFlags.DoNotWrapExceptions,
                binder: null,
                [synapse, cancellationToken],
                culture: null)!;
        }

        return handlers;
    }

    private static bool IsJournalProjection(Type synapseType)
        => ProjectionFacts.GetOrAdd(
            synapseType,
            static type => type.GetCustomAttribute<JournalProjectionAttribute>() is not null);

    private static bool GrainIdEquals(GrainId? left, GrainId? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Value.Equals(right.Value);
    }

    internal readonly struct ClientEntryCorrelationScope : IDisposable
    {
        private readonly Neuron _neuron;
        private readonly CorrelationId? _previous;

        public ClientEntryCorrelationScope(Neuron neuron, CorrelationId? previous)
        {
            _neuron = neuron;
            _previous = previous;
        }

        public void Dispose() => _neuron._ambientClientCorrelation = _previous;
    }

    internal readonly record struct CapabilityTurn(SynapseDelivery? PreviousHandling);
}
