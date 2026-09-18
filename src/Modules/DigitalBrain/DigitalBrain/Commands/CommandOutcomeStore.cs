using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// Dedup is exact for the last 1024 resolved commands and nothing beyond; unresolved commands are
// never dropped so an in-flight id can never be reused silently. Admission stops at 1024 unresolved commands.
internal sealed class CommandOutcomeStore(IDurableDictionary<CommandId, CommandOutcome> outcomes)
{
    internal const int MaxResolved = 1024;
    internal const int MaxUnresolved = 1024;
    private int _resolvedCount;
    private int _unresolvedCount;

    internal void NoteReloaded()
    {
        _resolvedCount = 0;
        _unresolvedCount = 0;
        foreach (var entry in outcomes)
        {
            AdjustCount(entry.Value, 1);
        }
    }

    internal CommandOutcome? Find(CommandId id) => outcomes.TryGetValue(id, out var outcome) ? outcome : null;

    internal void Record(CommandId id, CommandOutcome outcome)
    {
        var previous = Find(id);
        outcomes[id] = outcome;
        if (previous is not null)
        {
            AdjustCount(previous, -1);
        }

        AdjustCount(outcome, 1);
        if (outcome.Phase == CommandPhase.Attempted || _resolvedCount <= MaxResolved)
        {
            return;
        }

        KeyValuePair<CommandId, CommandOutcome>? oldest = null;
        foreach (var entry in outcomes)
        {
            if (entry.Value.Phase != CommandPhase.Attempted
                && (oldest is null || entry.Value.Sequence < oldest.Value.Value.Sequence))
            {
                oldest = entry;
            }
        }

        if (oldest is { } evicted)
        {
            outcomes.Remove(evicted.Key);
            _resolvedCount--;
        }
    }

    // Unresolved outcomes are never evicted; the 1024 bound prevents unbounded growth when commands stop resolving.
    internal bool IsFullOfUnresolved
        => _unresolvedCount >= MaxUnresolved;

    internal IReadOnlyList<KeyValuePair<CommandId, CommandOutcome>> Unresolved()
        => [.. outcomes.Where(entry => entry.Value.Phase == CommandPhase.Attempted)];

    internal bool Reconcile(CommandJournal journal, DateTimeOffset at)
    {
        var unresolved = Unresolved();
        if (unresolved.Count == 0)
        {
            return false;
        }

        var terminal = journal.TerminalRecords();
        var changed = false;
        foreach (var (id, outcome) in unresolved)
        {
            if (terminal.Contains((id, outcome.Incarnation)))
            {
                continue;
            }

            var record = journal.Append(new(
                0, id, outcome.Incarnation, outcome.Interface, outcome.Method, CommandPhase.Unknown,
                outcome.Caller, CorrelationId.New(), null, null, null, null, at));
            Record(id, outcome with { Phase = CommandPhase.Unknown, Sequence = record.Sequence });
            changed = true;
        }

        return changed;
    }

    private void AdjustCount(CommandOutcome outcome, int delta)
    {
        if (outcome.Phase == CommandPhase.Attempted)
        {
            _unresolvedCount += delta;
        }
        else
        {
            _resolvedCount += delta;
        }
    }
}
