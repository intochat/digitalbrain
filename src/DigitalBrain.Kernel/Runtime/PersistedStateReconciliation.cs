using Orleans.Runtime;

namespace DigitalBrain.Kernel;

// Thrown when a persisted-state write fails and the follow-up recovery read also fails, so whether the
// write landed durably cannot be determined. The activation holding the persistent state must not be
// trusted for further reads until it is reactivated.
public sealed class PersistedStateWriteOutcomeUnknownException(Exception writeFailure, Exception recoveryFailure)
    : InvalidOperationException(
        "Persisted-state write outcome is unknown; the recovery read also failed.",
        new AggregateException(writeFailure, recoveryFailure));

public static class PersistedStateReconciliation
{
    // If the write throws, re-reads storage to find out what actually landed instead of blindly rolling
    // back the in-memory copy -- an exception can be thrown after a write has already durably committed
    // (e.g. a lost acknowledgement), and blindly reverting in that case would let a caller re-execute an
    // already-succeeded effect.
    public static async Task WriteWithRollbackAsync<TPersisted>(
        IPersistentState<TPersisted> persistentState,
        TPersisted next,
        Func<TPersisted, TPersisted, bool> sameState)
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
            if (persistentState.RecordExists == previousExists &&
                string.Equals(persistentState.Etag, previousEtag, StringComparison.Ordinal) &&
                sameState(persistentState.State, previousState))
            {
                throw;
            }
            throw new InvalidOperationException(
                "Persisted-state write failed after the durable concurrency state advanced; the refreshed state was retained.",
                writeFailure);
        }
    }
}
