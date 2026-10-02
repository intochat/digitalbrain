using DigitalBrain.Contracts.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Registry;

[Alias("registry.observer"), Orleans.Metadata.DefaultGrainType("registry")]
internal interface IRegistryObserver : IGrainWithStringKey
{
    Task Observe(NeuronActivity activity);
    // Activity arrives in bursts (every activation in an intent); one call and one state write
    // per burst keeps the registry's bookkeeping out of the intent's span and storage budget.
    [Alias("ObserveBatch")]
    Task ObserveBatch(NeuronActivity[] activities);
}

[GenerateSerializer, Alias("registry.state")]
internal sealed record RegistryState
{
    [Id(0)] public Dictionary<string, NeuronInstance> Instances { get; init; } = new(StringComparer.Ordinal);
}

[GrainType("registry")]
internal sealed class RegistryNeuron(NeuronTypes types, NeuronTypeSearch search,
    [PersistentState("registry", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<RegistryState> store)
    : Neuron, IRegistry, IRegistryObserver
{
    public Task<IReadOnlyList<NeuronType>> Types() => Task.FromResult(types.Read());

    public Task<IReadOnlyList<NeuronTypeHit>> Search(string query, int take = 10, CancellationToken cancellationToken = default)
        => search.Search(query, take, cancellationToken);

    public Task<IReadOnlyList<NeuronInstance>> Instances(string? typeId = null, bool activeOnly = false, int skip = 0, int take = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        return Task.FromResult<IReadOnlyList<NeuronInstance>>([.. store.State.Instances.Values
            .Where(instance => (typeId is null || instance.TypeIds.Contains(typeId, StringComparer.Ordinal)) && (!activeOnly || instance.LastKnownActive))
            .OrderBy(instance => instance.Id, StringComparer.Ordinal).Skip(skip).Take(Math.Min(take, 1000))]);
    }

    public Task Observe(NeuronActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return ObserveBatch([activity]);
    }

    public async Task ObserveBatch(NeuronActivity[] activities)
    {
        ArgumentNullException.ThrowIfNull(activities);
        var previous = store.State;
        var instances = new Dictionary<string, NeuronInstance>(previous.Instances, StringComparer.Ordinal);
        var changed = false;
        foreach (var activity in activities)
        {
            ArgumentNullException.ThrowIfNull(activity);
            ArgumentException.ThrowIfNullOrWhiteSpace(activity.NeuronId);
            instances.TryGetValue(activity.NeuronId, out var existing);
            var active = activity is NeuronActivated;
            if (existing is not null && (activity.ObservedAt < existing.LastSeenAt
                || (active && activity.ActivationId == existing.ActivationId && !existing.LastKnownActive)
                || (!active && activity.ActivationId != existing.ActivationId)
                || (activity.ActivationId == existing.ActivationId && activity.ObservedAt == existing.LastSeenAt && existing.LastKnownActive == active)))
            { continue; }
            instances[activity.NeuronId] = new NeuronInstance(activity.NeuronId, activity.Key, activity.TypeIds.ToArray(), activity.ActivationId,
                existing?.FirstSeenAt ?? activity.ObservedAt, activity.ObservedAt, active);
            changed = true;
        }
        if (!changed) { return; }
        store.State = previous with { Instances = instances };
        try { await store.WriteStateAsync(); }
        catch { store.State = previous; throw; }
    }
}
