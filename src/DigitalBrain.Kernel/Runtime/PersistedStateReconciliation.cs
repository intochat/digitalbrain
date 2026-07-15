using Orleans.Runtime;
namespace DigitalBrain.Kernel;

internal sealed class PersistedStateWriteOutcomeUnknownException(Exception writeFailure, Exception recoveryFailure)
    : InvalidOperationException("Persisted-state write outcome is unknown; the recovery read also failed.", new AggregateException(writeFailure, recoveryFailure));
internal static class PersistedStateReconciliation
{
    public static async Task WriteWithRollbackAsync<TPersisted>(IPersistentState<TPersisted> persistentState, TPersisted next, Func<TPersisted, TPersisted, bool> sameState)
    {
        var previousState = persistentState.State;
        var previousEtag = persistentState.Etag;
        var previousExists = persistentState.RecordExists;
        persistentState.State = next;
        try
        {
            await persistentState.WriteStateAsync().ConfigureAwait(false);
        }
        catch (Exception writeFailure)
        {
            try
            {
                await persistentState.ReadStateAsync().ConfigureAwait(false);
            }
            catch (Exception recoveryFailure)
            {
                throw new PersistedStateWriteOutcomeUnknownException(writeFailure, recoveryFailure);
            }
            if (persistentState.RecordExists && sameState(persistentState.State, next))
                return;
            if (persistentState.RecordExists == previousExists && string.Equals(persistentState.Etag, previousEtag, StringComparison.Ordinal) &&
                sameState(persistentState.State, previousState))
            {
                throw;
            }
            throw new InvalidOperationException("Persisted-state write failed after the durable concurrency state advanced; the refreshed state was retained.", writeFailure);
        }
    }

    public static async Task ClearWithReconciliationAsync<TPersisted>(IPersistentState<TPersisted> persistentState)
    {
        var previousState = persistentState.State;
        var previousEtag = persistentState.Etag;
        var previousExists = persistentState.RecordExists;
        try
        {
            await persistentState.ClearStateAsync().ConfigureAwait(false);
        }
        catch (Exception clearFailure)
        {
            try
            {
                await persistentState.ReadStateAsync().ConfigureAwait(false);
            }
            catch (Exception recoveryFailure)
            {
                throw new PersistedStateWriteOutcomeUnknownException(clearFailure, recoveryFailure);
            }
            if (!persistentState.RecordExists)
                return;
            if (persistentState.RecordExists == previousExists && string.Equals(persistentState.Etag, previousEtag, StringComparison.Ordinal) &&
                EqualityComparer<TPersisted>.Default.Equals(persistentState.State, previousState))
                throw;
            throw new InvalidOperationException("Persisted-state clear failed after the durable concurrency state advanced; the refreshed state was retained.", clearFailure);
        }
    }
}
