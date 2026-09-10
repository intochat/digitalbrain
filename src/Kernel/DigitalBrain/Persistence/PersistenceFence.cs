using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class PersistenceFence(NeuronId neuron, IJournaledStateManager stateManager,
    CancellationToken activation, Func<bool> reconcile, Action deactivateOnIdle, Action noteCommitted)
{
    private bool _faulted;
    private bool _storageHoldsJournal;

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

    internal async Task PersistAsync()
    {
        Guard();
        try
        {
            await stateManager.WriteStateAsync(activation).ConfigureAwait(true);
            noteCommitted();
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
        await RecoverAsync(cause).ConfigureAwait(true);
    }

    private async Task RecoverAsync(Exception cause)
    {
        try
        {
            await stateManager.RevertPendingChangesAsync(activation).ConfigureAwait(true);
            noteCommitted();
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
                await stateManager.WriteStateAsync(activation).ConfigureAwait(true);
                noteCommitted();
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
