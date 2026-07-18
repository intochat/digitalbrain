using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public sealed class NeuronMemoryGrain(
    [PersistentState("neuron-memory", "digitalbrain")] IPersistentState<NeuronMemoryState> state)
    : Grain, INeuronMemoryGrain
{
    public Task<byte[]?> GetEncryptedMemoryAsync() => Task.FromResult(state.State.Bytes);

    public async Task SaveEncryptedMemoryAsync(byte[] encryptedBytes)
    {
        state.State.Bytes = encryptedBytes;
        await state.WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed class NeuronMemoryState
{
    [Id(0)] public byte[]? Bytes { get; set; }
}
