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

    internal void Promote(BrainActivityId activation)
    {
        foreach (var key in _history.Keys.ToArray())
        {
            var history = _history[key];
            var current = history[^1];
            if (current.Status != SynapseRevisionStatus.Staged || current.Activation != activation)
            {
                continue;
            }

            var definition = current.Definition with { Revision = current.Revision + 1 };
            Add(new SynapseRevision(definition, current.OutputContract, SynapseRevisionStatus.Live, null, activation));
        }
    }

    internal IEnumerable<SynapseRevision> LatestFor(
        EndpointAddress source,
        Brain.Abstractions.Contracts.ContractId contract,
        Func<Brain.Abstractions.Identity.BrainActivityId, bool> isActivationActive)
        => _history.Values
            .Select(history => VisibleRevision(history, isActivationActive))
            .Where(revision => revision is not null
                && revision.Source == source
                && revision.Contract == contract)
            .Select(static revision => revision!);

    private static SynapseRevision? VisibleRevision(
        ImmutableList<SynapseRevision> history,
        Func<Brain.Abstractions.Identity.BrainActivityId, bool> isActivationActive)
    {
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var revision = history[index];
            if (revision.Status == SynapseRevisionStatus.Retired)
            {
                // Retirement is terminal for a stable route; an incomplete
                // replacement must never resurrect what it superseded.
                return null;
            }

            if (revision.Status == SynapseRevisionStatus.Live
                && (revision.Activation is null || isActivationActive(revision.Activation.Value)))
            {
                return revision;
            }
        }

        return null;
    }
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
