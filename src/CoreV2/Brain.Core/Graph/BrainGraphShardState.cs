using System.Collections.Immutable;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Core.Endpoints;

namespace Brain.Core.Graph;

internal sealed class BrainGraphShardState
{
    private readonly Dictionary<SynapseKey, ImmutableList<SynapseRevision>> _history = [];
    private readonly Dictionary<StableRoute, SynapseKey> _stableKeys = [];

    internal int RevisionCount => _history.Values.Sum(static revisions => revisions.Count);

    internal bool TryGetKey(StableRoute route, out SynapseKey key)
        => _stableKeys.TryGetValue(route, out key);

    internal ImmutableList<SynapseRevision> History(SynapseKey key)
        => _history.TryGetValue(key, out var history)
            ? history
            : throw new KeyNotFoundException($"No synapse history exists for '{key}'.");

    internal void Add(SynapseRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var route = StableRoute.From(revision.Definition);
        if (!_history.TryGetValue(revision.Key, out var history))
        {
            _stableKeys.Add(route, revision.Key);
            _history.Add(revision.Key, [revision]);
            return;
        }

        if (history[^1].Revision + 1 != revision.Revision)
        {
            throw new InvalidOperationException("Synapse revisions must be appended contiguously.");
        }

        _history[revision.Key] = history.Add(revision);
    }

    internal IEnumerable<SynapseRevision> LatestFor(EndpointAddress source, Brain.Abstractions.Contracts.ContractId contract)
        => _history.Values
            .Select(static history => history[^1])
            .Where(revision => revision.Status == SynapseRevisionStatus.Live
                && revision.Source == source
                && revision.Contract == contract);
}

internal sealed record StableRoute(
    EndpointAddress Source,
    Brain.Abstractions.Contracts.ContractId Contract,
    string Scope,
    WiringSlotId Slot)
{
    internal static StableRoute From(SynapseDefinition definition)
        => new(definition.Source, definition.Contract, definition.Scope, definition.WiringSlot);
}
