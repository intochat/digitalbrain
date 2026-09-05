using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Core;

internal sealed class SignalSender
{
    private readonly NeuronId _source;
    private readonly TimeProvider _clock;
    private readonly SignalRouter _router;
    private readonly NeuronJournals _journals;
    private readonly NeuronSynapses _synapses;
    private readonly IGrainFactory _grains;
    private readonly Func<SignalDelivery, CancellationToken, Task<DeliveryOutcome>> _deliverLocally;
    private readonly Func<CancellationToken, ValueTask> _persist;

    internal SignalSender(
        NeuronId source,
        TimeProvider clock,
        SignalRouter router,
        NeuronJournals journals,
        NeuronSynapses synapses,
        IGrainFactory grains,
        Func<SignalDelivery, CancellationToken, Task<DeliveryOutcome>> deliverLocally,
        Func<CancellationToken, ValueTask> persist)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(journals);
        ArgumentNullException.ThrowIfNull(synapses);
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(deliverLocally);
        ArgumentNullException.ThrowIfNull(persist);

        _source = source;
        _clock = clock;
        _router = router;
        _journals = journals;
        _synapses = synapses;
        _grains = grains;
        _deliverLocally = deliverLocally;
        _persist = persist;
    }

    internal async Task<SignalDeliveryResult> SendAsync(
        NeuronId receiver,
        Signal signal,
        SignalDelivery? cause,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        var delivery = await RecordOutgoingAsync(signal, cause)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        // Bound the remote await here, inside the state-owning sender. A timeout around
        // SendAsync itself would leave this continuation alive to reinforce a route
        // after the caller's serialized turn had already unwound.
        var handling = DeliverAsync(receiver, delivery, DeliveryMode.Awaited, cancellationToken);
        // Local self-send shares our mutable activation state, so it must unwind
        // cooperatively before this turn can end. Only remote work can be detached.
        var outcome = await (receiver == _source ? handling : handling.WaitAsync(cancellationToken))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();

        if (outcome == DeliveryOutcome.Handled)
        {
            _synapses.Reinforce(receiver, signal.GetType().Name, SynapseKind.Learned);
            await _persist(CancellationToken.None)
                .ConfigureAwait(true);
        }

        return new SignalDeliveryResult(delivery, outcome);
    }

    internal async Task<int> BroadcastAsync(Signal signal, SignalDelivery? cause)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var receivers = _router.BroadcastRecipientsFor(signal, _source, _synapses)
            .Distinct()
            .ToArray();
        if (receivers.Length == 0)
        {
            return 0;
        }

        var correlation = cause?.CorrelationId ?? CorrelationId.New();
        var signalType = signal.GetType().Name;

        foreach (var receiver in receivers)
        {
            var delivery = await RecordOutgoingAsync(signal, cause, correlation)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            var outcome = await DeliverAsync(receiver, delivery, DeliveryMode.Awaited)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            if (outcome == DeliveryOutcome.Handled)
            {
                _synapses.Reinforce(receiver, signalType, SynapseKind.Learned);
                await _persist(CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }

        return receivers.Length;
    }

    internal async Task ReplyAsync(Signal response, SignalDelivery handling)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(handling);

        var delivery = await RecordOutgoingAsync(response, handling, handling.CorrelationId)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        using (NeuronRequestPath.Clear())
        {
            _ = ObserveDetachedAsync(handling.Caller, delivery);
        }
    }

    internal async Task<SignalDelivery> RecordOutgoingAsync(
        Signal signal,
        SignalDelivery? cause,
        CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var delivery = SignalDelivery.Create(
            signal,
            _source,
            _journals.OutgoingNextSequence,
            _clock,
            cause,
            correlation,
            principal: VerifiedActor.Current?.PrincipalId ?? cause?.Principal);

        _journals.AppendOutgoing(delivery);
        await _persist(CancellationToken.None)
            .ConfigureAwait(true);
        await _journals.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return delivery;
    }

    private Task<DeliveryOutcome> DeliverAsync(
        NeuronId receiver,
        SignalDelivery delivery,
        DeliveryMode mode,
        CancellationToken cancellationToken = default)
        // Same-activation Deliver is in-process: a serialized neuron cannot await its own
        // grain proxy. Incoming/outgoing call filters therefore do not see this path;
        // journal and synapse population stay here, not in a filter.
        => mode == DeliveryMode.Awaited && receiver == _source
            ? _deliverLocally(delivery, cancellationToken)
            : _grains.GetGrain<INeuronGrain>(receiver.ToGrainId()).Deliver(delivery, cancellationToken);

    private async Task ObserveDetachedAsync(NeuronId receiver, SignalDelivery delivery)
    {
        try
        {
            _ = await DeliverAsync(receiver, delivery, DeliveryMode.Detached)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception undelivered)
        {
            SignalTelemetry.ReplyDropped(_source, receiver, undelivered);
        }
    }

    private enum DeliveryMode : byte
    {
        Awaited,
        Detached,
    }
}
