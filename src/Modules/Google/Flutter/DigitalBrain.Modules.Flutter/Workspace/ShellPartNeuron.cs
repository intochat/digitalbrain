using System.Security.Cryptography;
using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Workspace;

[GrainType("intochat.shell-part")]
internal sealed class ShellPartNeuron([PersistentState("part", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ShellPartState> state) : Grain, IShellPart
{
    public async Task Put(string json)
    {
        if (state.State.Json == json) { return; }
        if (state.State.Json is not null) { throw new InvalidOperationException("Immutable workspace record cannot be replaced."); }
        state.State = new() { Json = json };
        try { await state.WriteStateAsync(); } catch { state.State = new(); throw; }
    }
    public Task<string> Read() => Task.FromResult(state.State.Json ?? throw new InvalidDataException("Workspace record is missing."));
}
