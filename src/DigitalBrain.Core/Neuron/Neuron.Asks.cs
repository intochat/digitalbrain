namespace DigitalBrain;

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
                journal.UnpinAsk(position);
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
