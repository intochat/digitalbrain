using System.Diagnostics;
using DigitalBrain.Abstractions;
using Orleans.Journaling;

using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Synapses;
namespace DigitalBrain.Core;

public abstract class Neuron :
    DurableGrain,
    INeuron,
    INeuronGrain,
    INeuronQuery
{
    private readonly NeuronActivationComponents _components;
    private readonly SignalSender _sender;
    private SignalDelivery? _handling;

    protected Neuron(NeuronRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _components = runtime.Bind(ServiceProvider, Id);
        _sender = new SignalSender(
            Id,
            _components.Clock,
            _components.Router,
            _components.Journal,
            _components.Synapses,
            GrainFactory,
            DispatchDeliveryAsync,
            WriteStateAsync);
    }

    public NeuronId Id
        => NeuronId.FromGrainKey(
            this.GetGrainId().Type.ToString()!,
            this.GetPrimaryKeyString());

    protected TimeProvider TimeProvider => _components.Clock;

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

    public async Task<DeliveryOutcome> Deliver(
        SignalDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        return await DispatchDeliveryAsync(delivery, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
        => Task.FromResult(_components.Journal.Read(kind, afterSequence));

    public Task<IReadOnlyList<Synapse>> ReadSynapses()
        => Task.FromResult(_components.Synapses.All());

    public async Task Watch(
        JournalKind kind,
        long afterSequence,
        IJournalObserver observer)
        => await _components.Journal.WatchAsync(kind, afterSequence, observer)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public Task Unwatch(IJournalObserver observer)
    {
        _components.Journal.Unwatch(observer);
        return Task.CompletedTask;
    }

    protected Task<SignalDeliveryResult> SendAsync(NeuronId receiver, Signal signal)
        => _sender.SendAsync(receiver, signal, _handling);

    protected Task<SignalDeliveryResult> PublishAsync<TNeuron, TSignal>(NeuronId to, TSignal signal)
        where TNeuron : INeuron, IHandle<TSignal>
        where TSignal : Signal
    {
        var expected = NeuronId.For<TNeuron>(to.Owner, to.Name);
        if (to != expected)
        {
            throw new ArgumentException(
                $"Neuron '{to}' is not a '{expected.Type}' instance.",
                nameof(to));
        }

        return SendAsync(to, signal);
    }

    protected Task<int> BroadcastAsync(Signal signal)
        => _sender.BroadcastAsync(signal, _handling);

    protected Task SubscribeToAsync<TSource, TSignal>(NeuronId source)
        where TSource : INeuron
        where TSignal : Signal
        => BindFromAsync(source, typeof(TSignal).Name, typeof(TSource));

    protected Task UnsubscribeFromAsync<TSource, TSignal>(NeuronId source)
        where TSource : INeuron
        where TSignal : Signal
        => UnbindFromAsync(source, typeof(TSignal).Name, typeof(TSource));

    protected Task ReplyAsync(Signal response)
        => _sender.ReplyAsync(
            response,
            _handling
                ?? throw new InvalidOperationException(
                    "ReplyAsync requires an active delivery context. Reply only from a HandleAsync turn."));

    protected Task<SignalDelivery> RecordOutgoingAsync(Signal signal)
        => _sender.RecordOutgoingAsync(signal, _handling);

    protected new IDisposable RegisterTimer(
        Func<object, Task> callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period)
        => throw new InvalidOperationException(
            $"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require "
            + "serialized turns.");

    private async Task<DeliveryOutcome> DispatchDeliveryAsync(
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
            var outcome = await _components.Dispatcher.DispatchAsync(
                    this,
                    delivery.Signal,
                    cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            _components.Journal.AppendIncoming(delivery);
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            await _components.Journal.NotifyWatchersAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return outcome;
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

    public Task HandleAsync(Subscribe signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return BindFromAsync(signal.Source, signal.SignalType, expectedSourceType: null);
    }

    public Task HandleAsync(Unsubscribe signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return UnbindFromAsync(signal.Source, signal.SignalType, expectedSourceType: null);
    }

    public async Task BindOutgoing(NeuronId subscriber, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        RequireSameOwner(subscriber);
        _components.Synapses.Bind(subscriber, signalType);
        await WriteStateAsync().ConfigureAwait(true);
    }

    public async Task UnbindOutgoing(NeuronId subscriber, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        RequireSameOwner(subscriber);
        _components.Synapses.Unbind(subscriber, signalType);
        await WriteStateAsync().ConfigureAwait(true);
    }

    private async Task BindFromAsync(NeuronId source, string signalType, Type? expectedSourceType)
    {
        RequireSubscription(source, signalType, expectedSourceType);
        await GrainFactory.GetGrain<INeuronGrain>(source.ToGrainId())
            .BindOutgoing(Id, signalType)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task UnbindFromAsync(NeuronId source, string signalType, Type? expectedSourceType)
    {
        RequireSubscription(source, signalType, expectedSourceType);
        await GrainFactory.GetGrain<INeuronGrain>(source.ToGrainId())
            .UnbindOutgoing(Id, signalType)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private void RequireSubscription(NeuronId source, string signalType, Type? expectedSourceType)
    {
        if (string.IsNullOrWhiteSpace(signalType))
        {
            throw new NeuronAuthorizationException($"Neuron '{Id}' refuses a subscription without a signal type.");
        }

        RequireSameOwner(source);
        if (expectedSourceType is not null
            && source.Type != NeuronId.GrainTypeNameOf(expectedSourceType).ToLowerInvariant())
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{source}' is not a '{expectedSourceType.Name}' instance.");
        }

        if (!CanHandle(signalType))
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{Id}' cannot subscribe to '{signalType}' because it does not IHandle it.");
        }
    }

    private void RequireSameOwner(NeuronId other)
    {
        if (other.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{Id}' refuses a foreign owner '{other.Owner}'.");
        }
    }

    private bool CanHandle(string signalType)
        => GetType().GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IHandle<>)
            && string.Equals(contract.GetGenericArguments()[0].Name, signalType, StringComparison.Ordinal));
}
