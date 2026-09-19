using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;

namespace DigitalBrain.Core;

public abstract partial class Neuron
{
    async Task INeuronInbox.Drain()
    {
        Guard();
        RequestContext.Clear();
        if (_components.Pending.Peek() is not { } head)
        {
            await FinishDrainAsync().ConfigureAwait(true);
            return;
        }

        var delivery = head.Delivery;
        await _retry.EnsureReminderAsync().ConfigureAwait(true);

        if (_components.Pending.IsCancelled(delivery.SignalId) || IsAppliedBy(delivery.SignalId))
        {
            _components.Pending.CompleteHead(head);
            await PersistAsync().ConfigureAwait(true);
            await FinishDrainAsync().ConfigureAwait(true);
            return;
        }

        RequestContext.Set(NeuronRequestKeys.Caller, delivery.Source.GetGrainId().ToString());
        RequestContext.Set(NeuronRequestKeys.Correlation, delivery.CorrelationId.ToString());
        RequestContext.Set(NeuronRequestKeys.Causation, delivery.SignalId.ToString());

        var previous = ReactionContext;
        var previousReacting = _reacting;
        using var reaction = CancellationTokenSource.CreateLinkedTokenSource(_activation.Token);
        ReactionContext = new DeliveryReaction(delivery);
        _reacting = (delivery.SignalId, reaction);
        try
        {
            try
            {
                await ReceiveAsync(delivery, reaction.Token).ConfigureAwait(true);
                if (HasBufferedAnnouncements)
                {
                    throw new InvalidOperationException(
                        $"Neuron '{Id}' announced without saving: announcements are saved with the snapshot.");
                }

                AdmitTurnWork();
            }
            catch (OperationCanceledException) when (_activation.Token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                if (!_components.Pending.IsCancelled(delivery.SignalId))
                {
                    await _fence.DiscardStagedChangesAsync(failure).ConfigureAwait(true);
                    _retry.ArmTimer();
                    return;
                }
            }

            _components.Pending.CompleteHead(head);
            await PersistAsync().ConfigureAwait(true);
        }
        finally
        {
            _turnWork.Clear();
            _reactionSaved = false;
            DiscardBufferedAnnouncements();
            ReactionContext = previous;
            _reacting = previousReacting;
        }

        await FinishDrainAsync().ConfigureAwait(true);
    }

    private async Task FinishDrainAsync()
    {
        var remaining = await TryDrainAnnouncementsAsync().ConfigureAwait(true);
        if (_activation.IsCancellationRequested)
        {
            return;
        }

        await _retry.AfterDrainAsync(remaining).ConfigureAwait(true);
        if (remaining)
        {
            _retry.ArmTimer();
        }

        if (_components.Pending.Peek() is not null)
        {
            Wake();
        }
    }

    private async Task<bool> TryDrainAnnouncementsAsync()
    {
        try
        {
            return await DrainAnnouncementsAsync(_activation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_activation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != RetryScheduler.ReminderName)
        {
            return Task.CompletedTask;
        }

        _retry.NoteTick();
        Wake();
        return Task.CompletedTask;
    }

    private void Wake() => GrainFactory.GetGrain<INeuronInbox>(this.GetGrainId()).Drain().Ignore();
}
