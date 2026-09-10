using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class PersistenceFence(NeuronId neuron, IJournaledStateManager stateManager,
    CancellationToken activation, Func<bool> reconcile, Action deactivateOnIdle, NeuronActivationComponents components)
{
    private bool _faulted;
    private bool _storageHoldsJournal;
    private Task? _reactionRollback;

    internal void NoteStoredState(bool present)
    {
        if (present)
        {
            _storageHoldsJournal = true;
        }
    }

    internal void Guard()
    {
        if (_faulted)
        {
            throw new NeuronRecoveringException(neuron,
                $"Neuron '{neuron}' is recovering from a failed write. Retry after it reactivates from storage.");
        }
    }

    // A failed write leaves the activation fenced and every call is refused.
    // A failed reaction's rollback is ordinary, so a caller waits for it to finish instead of being refused.
    internal async Task GuardAsync()
    {
        if (_reactionRollback is { } rollback)
        {
            await rollback.ConfigureAwait(true);
        }

        Guard();
    }

    internal async Task PersistAsync()
    {
        Guard();
        // This boundary limits what this write may mark committed, excluding a later interleaving Deliver's staging.
        var boundary = components.CaptureCommitBoundary();
        try
        {
            await stateManager.WriteStateAsync(activation).ConfigureAwait(true);
            components.NoteCommitted(boundary);
            _storageHoldsJournal = true;
        }
        catch (OperationCanceledException) when (activation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception cause)
        {
            _faulted = true;
            await RecoverAsync(cause).ConfigureAwait(true);
            throw new NeuronPersistenceException(neuron,
                $"Neuron '{neuron}' failed to persist; the change was rolled back. Retry the operation.", cause);
        }
    }

    internal async Task DiscardStagedChangesAsync(Exception cause)
    {
        _faulted = true;
        _reactionRollback = RecoverAsync(cause);
        try
        {
            await _reactionRollback.ConfigureAwait(true);
        }
        finally
        {
            _reactionRollback = null;
        }
    }

    private async Task RecoverAsync(Exception cause)
    {
        try
        {
            await stateManager.RevertPendingChangesAsync(activation).ConfigureAwait(true);
            components.NoteReloaded();
        }
        catch (Exception failure)
        {
            deactivateOnIdle();
            throw new NeuronRecoveringException(neuron,
                $"Neuron '{neuron}' failed to revert pending changes. Retry after it reactivates from storage.", new AggregateException(cause, failure));
        }

        // Orleans revert rebinds only journal streams that storage already holds.
        if (!_storageHoldsJournal)
        {
            deactivateOnIdle();
            throw new NeuronRecoveringException(neuron,
                $"Neuron '{neuron}' could not revert in place because storage holds no journal for it yet. Retry after it reactivates from storage.", cause);
        }

        try
        {
            if (reconcile())
            {
                var boundary = components.CaptureCommitBoundary();
                await stateManager.WriteStateAsync(activation).ConfigureAwait(true);
                components.NoteCommitted(boundary);
                _storageHoldsJournal = true;
            }
        }
        catch (Exception failure)
        {
            deactivateOnIdle();
            throw new NeuronRecoveringException(neuron,
                $"Neuron '{neuron}' failed to reconcile after reverting pending changes. Retry after it reactivates from storage.", new AggregateException(cause, failure));
        }

        _faulted = false;
    }
}
