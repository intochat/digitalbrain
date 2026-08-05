namespace DigitalBrain;

// The honest minimal outcome for "Disconnect removed the connected route, but the target
// kind still declares this fact at my context, so declaration routing resumes" (§3, the
// ghost rule's scope sentence). ConnectionRefused was deliberately NOT reused: nothing was
// refused — the disconnect succeeded, and this states the one consequence it did not undo.
// An appended Core kind (physics #10), an ordinary listenable fact like every outcome kind.
public sealed record DeclaredRouteSurvives(string Fact, NeuronId To) : Synapse;

// Core interception of Connect/Disconnect receptions (§3), before module dispatch — the
// catalog refuses module declarations for the reserved kinds, so interception is total.
// Every reception journals heard (deduped, at-least-once like any fact); a valid Connect
// is an idempotent set-add and a Disconnect an idempotent set-remove committed in the same
// one-batch turn; a failing Connect mutates nothing and answers the requester with a
// directed ConnectionRefused — the string-typo disease dies at the door, loudly.
public abstract partial class Neuron
{
    private Task ReceiveConnectAsync(Connect connect, SynapseMetadata metadata)
        => RunCoreReceptionAsync(connect, metadata, (heardFrom, _, now) =>
        {
            if (ConnectRefusalOf(connect) is { } reason)
            {
                return StageCoreSaid(
                    new ConnectionRefused(heardFrom.ToSynapseRef(), connect.Fact, connect.To, reason),
                    heardFrom,
                    now,
                    directedTo: metadata.Source);
            }

            journal.AddConnection(connect.Fact, connect.To);   // idempotent: set semantics
            return false;
        });

    private Task ReceiveDisconnectAsync(Disconnect disconnect, SynapseMetadata metadata)
        => RunCoreReceptionAsync(disconnect, metadata, (heardFrom, _, now) =>
        {
            if (!journal.RemoveConnection(disconnect.Fact, disconnect.To))
            {
                return false;   // no such row: the journaled reception IS the no-op record
            }

            return catalog.TryGetFactType(disconnect.Fact, out var factType)
                && catalog.ListenerKindsOf(factType).Contains(disconnect.To.Kind)
                && StageCoreSaid(
                    new DeclaredRouteSurvives(disconnect.Fact, new NeuronId(disconnect.To.Kind, Id.Name)),
                    heardFrom,
                    now);
        });

    // Validation at handling time, against the local catalog — no lookup hazard (§3).
    private string? ConnectRefusalOf(Connect connect)
    {
        if (!catalog.TryGetFactType(connect.Fact, out var factType))
        {
            return $"'{connect.Fact}' is not a fact kind in the running catalog";
        }

        if (Catalog.ReplyTypeOrNull(factType) is not null)
        {
            return $"'{connect.Fact}' is a question; questions are not connectable — "
                + "a connected second answerer instance would mint duplicate replies";
        }

        if (!catalog.TryGetNeuronType(connect.To.Kind, out _))
        {
            return $"'{connect.To.Kind}' is not a neuron kind in the running catalog";
        }

        return catalog.ListenerKindsOf(factType).Contains(connect.To.Kind)
            ? null
            : $"'{connect.To.Kind}' does not declare INeuron<{factType.Name}>";
    }

    // The Core-owned reception turn shared by every reserved kind: heard entry, the kind's
    // own staging, watermark, ONE commit — poisoning on any failure exactly like a module
    // turn, then the schedule timers resync against the committed table.
    private async Task RunCoreReceptionAsync(
        Synapse fact,
        SynapseMetadata metadata,
        Func<SynapseRefEntry, long, DateTimeOffset, bool> stageOutcome)
    {
        bool deliverable;
        try
        {
            var now = clock.GetUtcNow();
            var heardFrom = SynapseRefEntry.From(new SynapseRef(metadata.Source, metadata.Sequence));
            var heardPosition = journal.AppendHeard(
                catalog.KindOfFact(fact.GetType()),
                metadata.Timestamp,
                heardFrom,
                metadata.Cause is { } cause ? SynapseRefEntry.From(cause) : null,
                metadata.Answers is { } answers ? SynapseRefEntry.From(answers) : null,
                codec.Encode(fact));
            deliverable = stageOutcome(heardFrom, heardPosition, now);
            journal.SetWatermark(metadata.Source, metadata.Sequence, now);
        }
        catch
        {
            Poison();
            throw;
        }

        await CommitCoreBatchAsync(deliverable);
        SyncScheduleTimers();
    }
}
