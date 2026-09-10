using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// Dedup is exact for the last 1024 resolved commands and nothing beyond; unresolved commands are
// never dropped so an in-flight id can never be reused silently. Admission stops at 1024 unresolved commands.
internal sealed class CommandDedup(IDurableDictionary<CommandId, CommandOutcome> outcomes)
{
    internal const int MaxResolved = 1024;

    internal CommandOutcome? Find(CommandId id) => outcomes.TryGetValue(id, out var outcome) ? outcome : null;

    internal void Record(CommandId id, CommandOutcome outcome)
    {
        outcomes[id] = outcome;
        if (outcome.Phase == CommandPhase.Attempted)
        {
            return;
        }

        var resolved = outcomes.Where(entry => entry.Value.Phase != CommandPhase.Attempted)
            .OrderBy(entry => entry.Value.Sequence).ToArray();
        foreach (var entry in resolved.Take(Math.Max(0, resolved.Length - MaxResolved)))
        {
            outcomes.Remove(entry.Key);
        }
    }

    // Unresolved outcomes are never evicted; the 1024 bound prevents unbounded growth when commands stop resolving.
    internal bool IsFullOfUnresolved
        => UnresolvedEntries.Count() >= MaxResolved;

    internal IReadOnlyList<KeyValuePair<CommandId, CommandOutcome>> Unresolved()
        => [.. UnresolvedEntries];

    private IEnumerable<KeyValuePair<CommandId, CommandOutcome>> UnresolvedEntries
        => outcomes.Where(entry => entry.Value.Phase == CommandPhase.Attempted);
}
