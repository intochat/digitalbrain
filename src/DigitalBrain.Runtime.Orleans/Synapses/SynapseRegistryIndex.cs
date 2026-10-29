using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

[GenerateSerializer]
internal sealed class SynapseRegistryIndexState
{
    [Id(0)]
    public List<string> Brains { get; set; } = [];
}

internal sealed class SynapseRegistryIndex(
    [PersistentState("synapse-index", IntoChatOSNames.ClusterGrainStorage)] IPersistentState<SynapseRegistryIndexState> state) : Grain, ISynapseRegistryIndex
{
    /// <summary>The single grain key.</summary>
    public const string Key = "index";

    public async Task Add(string brainKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(brainKey);
        if (state.State.Brains.Contains(brainKey, StringComparer.Ordinal)) { return; }
        state.State.Brains.Add(brainKey);
        await state.WriteStateAsync();
    }

    public async Task Remove(string brainKey)
    {
        if (state.State.Brains.RemoveAll(b => string.Equals(b, brainKey, StringComparison.Ordinal)) == 0) { return; }
        await state.WriteStateAsync();
    }

    public Task<string[]> List() => Task.FromResult(state.State.Brains.ToArray());
}
