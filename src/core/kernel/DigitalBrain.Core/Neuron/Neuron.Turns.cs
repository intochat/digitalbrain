using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Core;

public abstract partial class Neuron
{
    private static readonly ConcurrentDictionary<Type, bool> SettledFailureTypes = new();

    private bool HasAlreadyHandled(SynapseDelivery delivery)
        => _remembered.Contains(delivery.SynapseId);

    private static bool SettlesDelivery(Exception failure)
        => SettledFailureTypes.GetOrAdd(
            failure.GetType(),
            static type => type.GetCustomAttribute<SettledDeliveryFailureAttribute>() is not null);

    private void Remember(SynapseId delivered)
    {
        _handled.Add(delivered.Value);
        _remembered.Add(delivered);

        while (_handled.Count > RememberedDeliveryBound)
        {
            _remembered.Remove(new SynapseId(_handled[0]));
            _evictedWhileHandling.Add(_handled[0]);
            _handled.RemoveAt(0);
        }
    }

    private void ForgetHandled(SynapseDelivery delivery)
    {
        for (var index = _handled.Count - 1; index >= 0; index--)
        {
            if (_handled[index] == delivery.SynapseId.Value)
            {
                _handled.RemoveAt(index);

                break;
            }
        }

        for (var index = _evictedWhileHandling.Count - 1; index >= 0; index--)
        {
            _handled.Insert(0, _evictedWhileHandling[index]);
        }
    }

    private void RecallHandledDeliveries()
    {
        _remembered.Clear();

        foreach (var delivered in _handled)
        {
            _remembered.Add(new SynapseId(delivered));
        }
    }

    private Task DispatchAsync(Synapse synapse, CancellationToken cancellationToken)
        => SynapseDispatch.HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
            ? handler(this, synapse, cancellationToken)
            : OnUnboundSynapseAsync(synapse, cancellationToken);

    protected virtual Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private void FlushOutgoing()
    {
        foreach (var fired in _firedWhileHandling)
        {
            _outgoing.Append(fired);
        }

        _firedWhileHandling.Clear();
    }

    private void Restore(CapabilityTurn turn)
    {
        _turnRollbacks.Clear();
        _turnRollbacks.AddRange(turn.PreviousRollbacks);
        _handling = turn.PreviousHandling;
        CurrentDeliveryDepth = turn.PreviousDepth;
        _turnCheckpoint = turn.PreviousCheckpoint;
    }

    private void AdvanceTurnCheckpoint()
    {
        if (_turnCheckpoint is { } checkpoint)
        {
            _turnCheckpoint = checkpoint with
            {
                CommittedOutbox = _outbox.Count,
                Outgoing = _outgoing.Checkpoint(),
            };
            _turnRollbacks.Clear();
        }
    }

    private async Task CommitRetractionAsync()
    {
        try
        {
            await CommitAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception unretracted)
        {
            SynapseTelemetry.RetractionUncommitted(Id, unretracted);
        }
    }

    private void ProtectCommittedIncoming()
    {
        if (_turnCheckpoint is { } checkpoint)
        {
            _turnCheckpoint = checkpoint with { Incoming = _incoming.Checkpoint() };
        }
    }

    private void RollbackTurnState()
    {
        for (var index = _turnRollbacks.Count - 1; index >= 0; index--)
        {
            _turnRollbacks[index]();
        }

        _turnRollbacks.Clear();
    }

    private void StageInboundCause()
    {
        if (_handling is null
            || _turnCheckpoint is not { InboundCommitted: false } checkpoint)
        {
            return;
        }

        _incoming.Append(_handling);
        Remember(_handling.SynapseId);
        _turnCheckpoint = checkpoint with { InboundCommitted = true };
    }

    private Synapse Snapshot(Synapse synapse)
        => _synapses.Deserialize(_synapses.SerializeToArray(synapse));

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
