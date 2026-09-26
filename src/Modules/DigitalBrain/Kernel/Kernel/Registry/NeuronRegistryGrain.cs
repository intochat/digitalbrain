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
    public async Task Register(NeuronRegistration[] records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var version = this.GetPrimaryKeyString();
        if (state.State.Version.Length != 0)
        {
            if (!state.State.Records.SequenceEqual(records))
            { throw new InvalidOperationException("Conflicting registry records share a version."); }
            return;
        }
        var previous = state.State;
        state.State = new NeuronRegistryState { Version = version, Records = records.ToArray() };
        try { await state.WriteStateAsync(); }
        catch { state.State = previous; throw; }
    }

    public Task<NeuronRegistration[]> Read()
    {
        EnsureReady();
        return Task.FromResult(state.State.Records.ToArray());
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
