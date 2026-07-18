using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

[GrainType("neuronrfwstore")]
public sealed class NeuronRfwStoreGrain(
    [PersistentState("neuron-rfw", "digitalbrain")] IPersistentState<NeuronRfwStoreState> state)
    : Grain, INeuronRfwStoreGrain
{
    public async Task SaveLatestCardAsync(PersistedRfwCard card)
    {
        state.State.LatestCard = card;
        await state.WriteStateAsync();
    }

    public Task<PersistedRfwCard?> GetLatestCardAsync()
    {
        return Task.FromResult(state.State.LatestCard);
    }
}

[GenerateSerializer]
public sealed class NeuronRfwStoreState
{
    [Id(0)]
    public PersistedRfwCard? LatestCard { get; set; }
}
