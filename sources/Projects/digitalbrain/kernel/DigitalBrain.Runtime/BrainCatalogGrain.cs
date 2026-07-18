using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.Runtime;

public sealed class BrainCatalogGrain(
    [FromKeyedServices("registered")] IDurableDictionary<string, NeuronCatalogEntry> registered,
    [FromKeyedServices("neurons")] IDurableDictionary<string, CatalogedNeuron> neurons,
    [FromKeyedServices("activity")] IDurableList<Synapse> activity,
    [FromKeyedServices("baseCursor")] IDurableValue<long> baseCursor)
    : DurableGrain, IBrainCatalog
{
    public async Task RegisterAsync(NeuronCatalogEntry entry)
    {
        registered[entry.TypeFullName] = entry;
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<NeuronCatalogEntry>> ListRegisteredAsync()
    {
        IReadOnlyList<NeuronCatalogEntry> list = registered.Values.ToArray();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<CatalogedNeuron>> ListNeuronsAsync()
    {
        IReadOnlyList<CatalogedNeuron> list = neurons.Values.ToArray();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<Synapse>> SnapshotAsync(DateTimeOffset since)
    {
        IReadOnlyList<Synapse> list = since == default
            ? activity.ToArray()
            : activity.Where(s => s.Timestamp >= since).ToArray();
        return Task.FromResult(list);
    }

    public Task<SynapseSlice> WatchSinceAsync(long cursor)
    {
        var current = baseCursor.Value + activity.Count;
        var startIndex = Math.Max(0, (int)(cursor - baseCursor.Value));
        IReadOnlyList<Synapse> deltas = startIndex >= activity.Count
            ? Array.Empty<Synapse>()
            : activity.Skip(startIndex).ToArray();
        return Task.FromResult(new SynapseSlice(current, deltas));
    }
}
