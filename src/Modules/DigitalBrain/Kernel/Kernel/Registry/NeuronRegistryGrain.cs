using DigitalBrain.Contracts.Registry;
using Orleans.Runtime;

namespace DigitalBrain.Core.Registry;

[GenerateSerializer]
public sealed class NeuronRegistryState
{
    [Id(0)] public string Version { get; set; } = "";
    [Id(1)] public NeuronRegistration[] Records { get; set; } = [];
}

[GrainType("kernel.neuron-registry")]
public sealed class NeuronRegistryGrain(
    [PersistentState("neuron-registry", "Default")] IPersistentState<NeuronRegistryState> state)
    : Grain, INeuronRegistryGrain
{
    public async Task ReplaceSnapshot(NeuronRegistrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.Version))
        { throw new ArgumentException("A registry snapshot requires a version.", nameof(snapshot)); }
        if (snapshot.Version != this.GetPrimaryKeyString())
        { throw new ArgumentException("A registry snapshot must be published under its version key.", nameof(snapshot)); }
        if (snapshot.Version == state.State.Version)
        {
            if (!state.State.Records.SequenceEqual(snapshot.Records))
            { throw new InvalidOperationException("Conflicting registry records share a version."); }
            return;
        }
        var previous = state.State;
        state.State = new NeuronRegistryState { Version = snapshot.Version, Records = snapshot.Records.ToArray() };
        try { await state.WriteStateAsync(); }
        catch { state.State = previous; throw; }
    }

    public Task<NeuronRegistrySnapshot> Read()
    {
        EnsureReady();
        return Task.FromResult(new NeuronRegistrySnapshot
        {
            Version = state.State.Version,
            Records = state.State.Records.ToArray(),
        });
    }

    public Task<NeuronRegistration?> Find(string id)
    {
        EnsureReady();
        return Task.FromResult(state.State.Records.FirstOrDefault(record => record.Id == id));
    }

    private void EnsureReady()
    {
        if (state.State.Version.Length == 0)
        { throw new InvalidOperationException("Neuron registry startup registration has not completed."); }
    }
}
