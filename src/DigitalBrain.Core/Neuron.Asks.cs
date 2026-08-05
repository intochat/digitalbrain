namespace DigitalBrain;

// Ask lifetime, the expiry end (§5): pins expire after the AskHorizon inside a Core-owned
// serialized turn on the asker — the drain tick invokes this directly, never as bare
// timer-callback work, so a failed expiry commit poisons instead of vanishing into a
// swallowing timer. AskExpired journals as a said entry per ordinary routing (zero
// receivers is fine) and the pin releases in the same batch; a late reply thereafter
// journals as a plain reception and dispatches nothing — ReceiveReplyAsync's pin predicate
// already refuses it, so falling off the window is loud exactly once. The answerer-side
// cleanup on closure lives in StageSaid: the first TReply-typed emission of a later turn
// removes the open-ask row it answers; the registration side (pins, open asks, Answers
// stamping, guarded reconstruction) is the receive stage in Neuron.cs.
public abstract partial class Neuron
{
    private async Task ExpireAsksAsync()
    {
        var now = clock.GetUtcNow();
        var expired = journal.ExpiredAsks(now, DeliveryPolicy.AskHorizon);
        if (expired.Count == 0)
        {
            return;
        }

        var deliverable = false;
        try
        {
            foreach (var position in expired)
            {
                var askEntry = journal.EntryAt(position);
                deliverable |= StageCoreSaid(
                    new AskExpired(new SynapseRef(Id, position), askEntry?.Kind ?? "unknown"),
                    new SynapseRefEntry(Id.Kind, Id.Name, position),
                    now);
                journal.UnpinAsk(position);   // the compaction pin releases with the record
            }
        }
        catch
        {
            Poison();
            throw;
        }

        await CommitCoreBatchAsync(deliverable);
    }
}
