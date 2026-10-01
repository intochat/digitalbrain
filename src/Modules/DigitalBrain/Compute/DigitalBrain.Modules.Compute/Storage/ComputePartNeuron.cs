using DigitalBrain.Contracts;
using DigitalBrain.Compute.Usage;
using Orleans.Runtime;

namespace DigitalBrain.Compute.Storage;

[GrainType("compute.record-part")]
internal sealed class ComputePartNeuron([PersistentState("part", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ComputePartState> state) : Grain, IComputePart
{
    public async Task Put(string json)
    {
        if (UsagePaging.Hash(json) != this.GetPrimaryKeyString() || json.Length > 8 * 1024 * 1024)
        { throw new ArgumentException("Invalid immutable compute part."); }
        if (state.State.Json == json) { return; }
        if (state.State.Json is not null) { throw new InvalidOperationException("Compute records are immutable."); }
        state.State = new() { Json = json };
        try { await state.WriteStateAsync(); }
        catch { state.State = new(); throw; }
    }
    public Task<string> Read() => Task.FromResult(state.State.Json ?? throw new InvalidDataException("Missing compute record."));
}
