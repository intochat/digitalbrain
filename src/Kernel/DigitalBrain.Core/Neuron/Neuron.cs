using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

public abstract class Neuron :
    DurableGrain,
    INeuron,
    IOutboxDrain
{
    private const string OutboxName = "outbox";
    private const string HandledName = "handled";
    private const int RememberedDeliveries = 4096;

    private readonly NeuronCapabilityCoordinator _capabilities;
    private readonly NeuronJournal _journal;
    private readonly NeuronMessagePipeline _messages;
    private readonly NeuronOutbox _outbox;
    private readonly NeuronStreamRegistry _streams;
    private readonly NeuronTurnCoordinator _turn;

    protected Neuron()
    {
        TimeProvider =
            ServiceProvider.GetKeyedService<TimeProvider>(NeuronTime.ServiceKey)
            ?? System.TimeProvider.System;

        _journal = new NeuronJournal(this, ServiceProvider);
        _outbox = new NeuronOutbox(
            this,
            ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(OutboxName),
            ServiceProvider.GetRequiredService<Serializer<OutboxEntry>>());
        var deliveries = new NeuronDeliveryMemory(
            this,
            ServiceProvider.GetRequiredKeyedService<IDurableList<Guid>>(HandledName));
        _turn = new NeuronTurnCoordinator(
            this,
            _journal,
            _outbox,
            deliveries,
            ServiceProvider.GetRequiredService<Serializer<Synapse>>());
        _streams = new NeuronStreamRegistry(this);
        _messages = new NeuronMessagePipeline(this, _journal, _outbox, _turn, _streams);
        _capabilities = new NeuronCapabilityCoordinator(this, _journal, _outbox, _turn);
    }

    public NeuronId Id
        => NeuronId.FromGrainKey(
            this.GetGrainId().Type.ToString()!,
            this.GetPrimaryKeyString());

    protected TimeProvider TimeProvider { get; }

    internal virtual int RememberedDeliveryBound => RememberedDeliveries;

    internal IServiceProvider NeuronServices => ServiceProvider;

    internal IGrainFactory NeuronGrainFactory => GrainFactory;

    internal TimeProvider NeuronTimeProvider => TimeProvider;

    protected NeuronId? CurrentDeliveryCaller => _turn.Handling?.Caller;

    protected SynapseId? CurrentDeliverySynapseId => _turn.Handling?.SynapseId;

    // A handler stamping provenance needs the correlation of the request that asked for it;
    // the unforgeable half of a provenance record can only come from the delivery.
    protected CorrelationId? CurrentDeliveryCorrelation => _turn.Handling?.CorrelationId;

    protected int CurrentDeliveryDepth => _turn.CurrentDepth;

    protected CancellationToken TurnCancellationToken => _turn.CancellationToken;

    internal CorrelationId? AmbientClientEntryCorrelation => _streams.AmbientClientCorrelation;

    internal IReadOnlyList<Guid> BoundStreamedEnumerations => _streams.BoundEnumerations;

    internal int PendingStreamedCapabilityRequests => _streams.PendingCapabilityRequests;

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());

        await base.OnActivateAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _turn.Activate();
        await _outbox.ActivateAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await OnNeuronActivatedAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    protected virtual Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task Deliver(
        SynapseDelivery delivery,
        CancellationToken cancellationToken = default)
        => await _turn.DeliverAsync(delivery, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

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

    async Task IOutboxDrain.Drain()
        => await _outbox.DrainFromWakeupAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    protected Task FlushOutboxAsync(CancellationToken cancellationToken)
        => _outbox.FlushAsync(cancellationToken);

    protected Task<SynapseDelivery> SendAsync(NeuronId receiver, Synapse synapse)
        => _messages.SendAsync(receiver, synapse);

    protected Task EmitAsync(Synapse synapse)
        => _messages.EmitAsync(synapse);

    protected CorrelationId ResolveEmissionCorrelation()
        => _messages.ResolveEmissionCorrelation();

    protected Task EmitAsync(Synapse synapse, CorrelationId correlation)
        => _messages.EmitAsync(synapse, correlation);

    protected Task EmitAtDepthAsync(
        Synapse synapse,
        CorrelationId correlation,
        int deliveryDepth)
        => _messages.EmitAtDepthAsync(synapse, correlation, deliveryDepth);

    protected Task ReplyAsync(
        Synapse response,
        CancellationToken cancellationToken = default)
        => _messages.ReplyAsync(response, cancellationToken);

    protected void ValidateCapabilityCaller(NeuronId expectedCaller)
        => _turn.ValidateCapabilityCaller(expectedCaller);

    protected void EnlistTurnRollback(Action rollback)
        => _turn.EnlistRollback(rollback);

    // Refusing an unhandled synapse here is correct in principle but is NOT this slice's work:
    // ReplyAsync addresses the caller, and callers routinely have no IHandle for the reply
    // type, so refusing breaks every request/reply in the product. It belongs to the turn and
    // delivery hardening, with an explicit accept-list for reply sinks.
    protected virtual Task OnUnboundSynapseAsync(
        Synapse synapse,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    internal SynapseDelivery StageIncomingOutcome(Synapse outcome, SynapseDelivery cause)
        => _messages.StageIncomingOutcome(outcome, cause);

    protected new IDisposable RegisterTimer(
        Func<object, Task> callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period)
        => throw new InvalidOperationException(
            $"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require "
            + "serialized turns.");

    internal ClientEntryCorrelationScope EnterClientEntryCorrelation(CorrelationId correlation)
    {
        var previous = _streams.EnterClientCorrelation(correlation);
        return new ClientEntryCorrelationScope(this, previous);
    }

    internal void RegisterClientStreamCorrelation(Guid enumerationId, CorrelationId correlation)
        => _streams.RegisterClientCorrelation(enumerationId, correlation);

    internal bool TryGetClientStreamCorrelation(
        Guid enumerationId,
        out CorrelationId correlation)
        => _streams.TryGetClientCorrelation(enumerationId, out correlation);

    internal void ForgetClientStreamCorrelation(Guid enumerationId)
        => _streams.ForgetClientCorrelation(enumerationId);

    internal void BindStreamedEnumeration(Guid enumerationId, GrainId? initiator)
        => _streams.BindEnumeration(enumerationId, initiator);

    internal void RequireStreamedEnumerationInitiator(Guid enumerationId, GrainId? caller)
        => _streams.RequireEnumerationInitiator(enumerationId, caller);

    internal void ReleaseStreamedEnumeration(Guid enumerationId)
        => _streams.ReleaseEnumeration(enumerationId);

    internal Task<SynapseDelivery> BeginCapabilityRequestAsync(
        string contract,
        string method,
        NeuronId target)
        => _capabilities.BeginRequestAsync(contract, method, target);

    internal bool TryRegisterStreamedCapabilityRequest(
        Guid enumerationId,
        SynapseDelivery request)
        => _streams.TryRegisterCapabilityRequest(enumerationId, request);

    internal bool TryClaimStreamedCapabilityRequest(
        Guid enumerationId,
        out SynapseDelivery request)
        => _streams.TryClaimCapabilityRequest(enumerationId, out request);

    internal Task RecordCapabilityOutcomeAsync(
        CapabilityOutcome outcome,
        SynapseDelivery request)
        => _capabilities.RecordOutcomeAsync(outcome, request);

    internal Task RecordStreamedCapabilityRequestAsync(
        SynapseDelivery delivery,
        GrainId? source)
        => _capabilities.RecordStreamedRequestAsync(delivery, source);

    internal Task<CapabilityTurn> BeginIncomingCapabilityRequestAsync(
        SynapseDelivery delivery,
        GrainId? source)
        => _capabilities.BeginIncomingRequestAsync(delivery, source);

    internal Task CompleteIncomingCapabilityRequestAsync(CapabilityTurn turn)
        => _capabilities.CompleteIncomingRequestAsync(turn);

    internal Task FailIncomingCapabilityRequestAsync(CapabilityTurn turn)
        => _capabilities.FailIncomingRequestAsync(turn);

    internal Task DispatchSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
        => SynapseDispatch.HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
            ? handler(this, synapse, cancellationToken)
            : OnUnboundSynapseAsync(synapse, cancellationToken);

    internal ValueTask WriteNeuronStateAsync(CancellationToken cancellationToken)
        => WriteStateAsync(cancellationToken);

    internal IGrainTimer RegisterNeuronTimer(
        Func<CancellationToken, Task> callback,
        TimeSpan dueTime,
        TimeSpan period)
        => this.RegisterGrainTimer(callback, dueTime, period);

    internal readonly struct ClientEntryCorrelationScope : IDisposable
    {
        private readonly Neuron _neuron;
        private readonly CorrelationId? _previous;

        public ClientEntryCorrelationScope(Neuron neuron, CorrelationId? previous)
        {
            _neuron = neuron;
            _previous = previous;
        }

        public void Dispose() => _neuron._streams.RestoreClientCorrelation(_previous);
    }

    internal readonly record struct CapabilityTurn(
        int CommittedOutbox,
        NeuronFeedCheckpoint Outgoing,
        IReadOnlyList<Action> PreviousRollbacks,
        SynapseDelivery? PreviousHandling,
        int PreviousDepth,
        TurnCheckpoint? PreviousCheckpoint);

    internal readonly record struct TurnCheckpoint(
        int CommittedOutbox,
        bool InboundCommitted,
        NeuronFeedCheckpoint Incoming,
        NeuronFeedCheckpoint Outgoing);
}
