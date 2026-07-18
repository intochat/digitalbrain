using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Security;

namespace DigitalBrain.Kernel.Runtime;

public sealed class BrainCheckpointStoreGrain(
    [PersistentState("checkpoint", "digitalbrain")] IPersistentState<BrainCheckpointState> state,
    INeuronStateProtector? protector = null)
    : Grain, IBrainCheckpointStoreGrain
{
    private readonly INeuronStateProtector protector = protector ?? new PassThroughNeuronStateProtector();
    public Task<BrainCheckpoint?> GetCheckpointAsync()
    {
        if (state.State.EncryptedData is not { Length: > 0 })
            return Task.FromResult<BrainCheckpoint?>(null);

        var decrypted = protector.Unprotect(state.State.EncryptedData);
        var json = System.Text.Encoding.UTF8.GetString(decrypted);
        var checkpoint = System.Text.Json.JsonSerializer.Deserialize<BrainCheckpoint>(json);
        return Task.FromResult(checkpoint);
    }

    public async Task SaveCheckpointAsync(BrainCheckpoint checkpoint)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(checkpoint);
        var encrypted = protector.Protect(System.Text.Encoding.UTF8.GetBytes(json));
        state.State.EncryptedData = encrypted;
        await state.WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed class BrainCheckpointState
{
    [Id(0)] public byte[]? EncryptedData { get; set; }
}
