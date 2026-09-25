using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.CSharpExpert;

[GrainType("csharp-expert.profile")]
internal sealed class CodingProfileNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CodingProfileState> state)
    : Neuron, ICodingProfile
{
    [ReadOnly]
    public Task<CodingProfile> Read() => Task.FromResult(state.State?.Profile ?? CodingProfile.Default);

    public async Task Write(CodingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        state.State = new CodingProfileState(profile);
        await state.WriteStateAsync();
    }
}
