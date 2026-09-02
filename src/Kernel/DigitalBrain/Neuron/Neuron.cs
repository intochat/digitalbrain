using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Synapses;
namespace DigitalBrain.Core;

public abstract class Neuron :
    DurableGrain,
    INeuron
{
    private delegate Task HandlerInvoker(object neuron, Signal signal, CancellationToken cancellationToken);

    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<Type, HandlerInvoker>> HandlersByNeuronType = new();
    private readonly NeuronJournal _journal;
    private readonly SynapseSet _synapses;
    private SignalDelivery? _handling;

    protected Neuron()
    {
        TimeProvider =
            ServiceProvider.GetKeyedService<TimeProvider>(NeuronTime.ServiceKey)
            ?? System.TimeProvider.System;

        _journal = new NeuronJournal(this, ServiceProvider);
        _synapses = new SynapseSet(ServiceProvider, Id, TimeProvider);
    }

    public NeuronId Id
        => NeuronId.FromGrainKey(
            this.GetGrainId().Type.ToString()!,
            this.GetPrimaryKeyString());

    protected TimeProvider TimeProvider { get; }

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
        SignalDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        await DispatchDeliveryAsync(delivery, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
        => Task.FromResult(_journal.Read(kind, afterSequence));

    public Task<IReadOnlyList<Synapse>> ReadSynapses() => Task.FromResult(_synapses.All());

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

    protected async Task<SignalDelivery> SendAsync(NeuronId receiver, Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var delivery = await StageOutgoingAsync(signal, _handling)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await DeliverToAsync(receiver, delivery)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return delivery;
    }

    // Directed fire: deliver, then record the edge. The synapse is written only after Deliver
    // returns, so a receiver that threw never strengthens the path to itself.
    protected async Task<SignalDelivery> FireAsync(NeuronId receiver, Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var delivery = await SendAsync(receiver, signal)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _synapses.Record(receiver, signal.GetType().Name, SynapseKind.Learned);
        await WriteStateAsync().ConfigureAwait(true);

        return delivery;
    }

    protected Task EmitAsync(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return EmitAsync(signal, ResolveEmissionCorrelation());
    }

    protected CorrelationId ResolveEmissionCorrelation()
        => _handling?.CorrelationId
            ?? CorrelationId.New();

    protected async Task EmitAsync(Signal signal, CorrelationId correlation)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var delivery = await StageOutgoingAsync(signal, _handling, correlation)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _ = delivery;
    }

    protected async Task ReplyAsync(
        Signal response,
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

    // Refusing an unhandled signal here is correct in principle but is NOT this slice's work:
    // ReplyAsync addresses the caller, and callers routinely have no IHandle for the reply
    // type, so refusing breaks every request/reply in the product. It belongs to the turn and
    // delivery hardening, with an explicit accept-list for reply sinks.
    protected virtual Task OnUnboundSignalAsync(
        Signal signal,
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

    internal async Task<SignalDelivery> StageOutgoingAsync(
        Signal signal,
        SignalDelivery? cause,
        CorrelationId? correlation = null)
    {
        var delivery = SignalDelivery.Create(
            signal,
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
        SignalDelivery delivery,
        CancellationToken cancellationToken)
    {
        using var handling = SignalTelemetry.Source.StartActivity("handle");

        handling?.SetTag(SignalTelemetry.ReceiverTag, Id.ToString());
        handling?.SetTag(SignalTelemetry.SignalTag, delivery.Signal.GetType().Name);
        handling?.SetTag(SignalTelemetry.CorrelationTag, delivery.CorrelationId.ToString());

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
            await DispatchSignalAsync(delivery.Signal, cancellationToken)
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

    private Task DeliverToAsync(NeuronId receiver, SignalDelivery delivery)
        => receiver == Id
            // A grain call to self would deadlock the serialized turn; dispatch in place.
            ? DispatchDeliveryAsync(delivery, CancellationToken.None)
            : GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).Deliver(delivery);

    private async Task DeliverReplyAsync(NeuronId receiver, SignalDelivery delivery)
    {
        try
        {
            await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId())
                .Deliver(delivery)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception undelivered)
        {
            SignalTelemetry.ReplyDropped(Id, receiver, undelivered);
        }
    }

    private Task DispatchSignalAsync(Signal signal, CancellationToken cancellationToken)
        => HandlersFor(GetType()).TryGetValue(signal.GetType(), out var handler)
            ? handler(this, signal, cancellationToken)
            : OnUnboundSignalAsync(signal, cancellationToken);

    private static IReadOnlyDictionary<Type, HandlerInvoker> HandlersFor(Type neuronType)
        => HandlersByNeuronType.GetOrAdd(neuronType, static type => BuildHandlers(type));

    private static Dictionary<Type, HandlerInvoker> BuildHandlers(Type neuronType)
    {
        var handlers = new Dictionary<Type, HandlerInvoker>();

        foreach (var handled in neuronType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IHandle<>)))
        {
            var signalType = handled.GetGenericArguments()[0];
            var handleMethod = handled.GetMethod(nameof(IHandle<>.HandleAsync))
                ?? throw new MissingMethodException(handled.FullName, nameof(IHandle<>.HandleAsync));

            handlers[signalType] = (neuron, signal, cancellationToken) => (Task)handleMethod.Invoke(
                neuron,
                BindingFlags.DoNotWrapExceptions,
                binder: null,
                [signal, cancellationToken],
                culture: null)!;
        }

        return handlers;
    }

}
