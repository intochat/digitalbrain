using DigitalBrain.Abstractions.Identity;
using Orleans.Journaling;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.v3.neuron-recovering")]
public sealed class NeuronRecoveringException(NeuronId neuron, string message, Exception? cause = null)
    : InvalidOperationException(message, cause)
{
    [Id(0)] public NeuronId Neuron { get; } = neuron;
}

[GenerateSerializer]
[Alias("db.v3.neuron-persistence")]
public sealed class NeuronPersistenceException(NeuronId neuron, string message, Exception cause)
    : InvalidOperationException(message, cause)
{
    [Id(0)] public NeuronId Neuron { get; } = neuron;
}

internal sealed class PersistenceFence(NeuronId neuron, IJournaledStateManager stateManager,
    CancellationToken activation, Func<bool> reconcile, Action deactivateOnIdle)
{
    private bool _faulted;
    private bool _storageHoldsJournal;

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
            _storageHoldsJournal = true;
        }
        catch (OperationCanceledException) when (activation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception cause)
        {
            _faulted = true;
            await RecoverAsync().ConfigureAwait(true);
            throw new NeuronPersistenceException(neuron,
                $"Neuron '{neuron}' failed to persist; the change was rolled back. Retry the operation.", cause);
        }
    }

    internal async Task DiscardStagedChangesAsync()
    {
        _faulted = true;
        await RecoverAsync().ConfigureAwait(true);
    }

    private async Task RecoverAsync()
    {
        try
        {
            await stateManager.RevertPendingChangesAsync(activation).ConfigureAwait(true);
        }
        catch (Exception failure)
        {
            deactivateOnIdle();
            throw new NeuronRecoveringException(neuron,
                $"Neuron '{neuron}' failed to revert pending changes. Retry after it reactivates from storage.", failure);
        }

        // Orleans revert rebinds only journal streams that storage already holds.
        if (!_storageHoldsJournal)
        {
            deactivateOnIdle();
            throw new NeuronRecoveringException(neuron,
                $"Neuron '{neuron}' could not revert in place because storage holds no journal for it yet. Retry after it reactivates from storage.");
        }

        try
        {
            if (reconcile())
            {
                await stateManager.WriteStateAsync(activation).ConfigureAwait(true);
                _storageHoldsJournal = true;
            }
        }
        catch (Exception failure)
        {
            deactivateOnIdle();
            throw new NeuronRecoveringException(neuron,
                $"Neuron '{neuron}' failed to reconcile after reverting pending changes. Retry after it reactivates from storage.", failure);
        }

        _faulted = false;
    }
}
