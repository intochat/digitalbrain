using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// Dedup is exact for the last 1024 resolved commands and nothing beyond; unresolved commands are
// never dropped so an in-flight id can never be reused silently. Admission stops at 1024 unresolved commands.
internal sealed class CommandDedup(IDurableDictionary<CommandId, CommandOutcome> outcomes)
{
    internal const int MaxResolved = 1024;
    private int _resolvedCount;
    private int _unresolvedCount;

    internal void NoteReloaded()
    {
        _resolvedCount = 0;
        _unresolvedCount = 0;
        foreach (var entry in outcomes)
        {
            if (entry.Value.Phase == CommandPhase.Attempted)
            {
                _unresolvedCount++;
            }
            else
            {
                _resolvedCount++;
            }
        }
    }

    internal CommandOutcome? Find(CommandId id) => outcomes.TryGetValue(id, out var outcome) ? outcome : null;

    internal void Record(CommandId id, CommandOutcome outcome)
    {
        var previous = Find(id);
        outcomes[id] = outcome;
        if (previous is not null)
        {
            if (previous.Phase == CommandPhase.Attempted)
            {
                _unresolvedCount--;
            }
            else
            {
                _resolvedCount--;
            }
        }

        if (outcome.Phase == CommandPhase.Attempted)
        {
            _unresolvedCount++;
            return;
        }

        _resolvedCount++;
        if (_resolvedCount > MaxResolved)
        {
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
    }

    // Unresolved outcomes are never evicted; the 1024 bound prevents unbounded growth when commands stop resolving.
    internal bool IsFullOfUnresolved
        => _unresolvedCount >= MaxResolved;

    internal IReadOnlyList<KeyValuePair<CommandId, CommandOutcome>> Unresolved()
        => [.. UnresolvedEntries];

    private IEnumerable<KeyValuePair<CommandId, CommandOutcome>> UnresolvedEntries
        => outcomes.Where(entry => entry.Value.Phase == CommandPhase.Attempted);
}
