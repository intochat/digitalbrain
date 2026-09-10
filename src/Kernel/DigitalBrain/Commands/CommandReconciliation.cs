using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

internal static class CommandReconciliation
{
    // Runs on every activation and again inside in-place recovery while the fence is held. An Attempted entry with
    // no terminal record in the window means the process died between the two persists: the command
    // was attempted and nothing it did committed, so it resolves as Unknown and a retry with the same
    // id re-executes as the next incarnation.
    internal static bool Reconcile(CommandJournal journal, CommandDedup dedup, DateTimeOffset at)
    {
        var changed = false;
        foreach (var (id, outcome) in dedup.Unresolved())
        {
            if (journal.HasTerminalRecord(id, outcome.Incarnation))
            {
                continue;
            }

            var record = journal.Append(new(
                0, id, outcome.Incarnation, outcome.Interface, outcome.Method, CommandPhase.Unknown,
                outcome.Caller, CorrelationId.New(), null, null, null, null, at));
            dedup.Record(id, outcome with { Phase = CommandPhase.Unknown, Sequence = record.Sequence });
            changed = true;
        }

        return changed;
    }
}
