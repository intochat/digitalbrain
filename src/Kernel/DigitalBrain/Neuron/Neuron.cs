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

    protected Task<int> BroadcastAsync(Signal signal)
        => _sender.BroadcastAsync(signal, _handling);

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

}
