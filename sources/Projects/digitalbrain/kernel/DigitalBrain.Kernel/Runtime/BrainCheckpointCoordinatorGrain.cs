using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public sealed class BrainCheckpointCoordinatorGrain : Grain, IBrainCheckpointCoordinatorGrain
{
    public async Task<Guid> CreateCheckpointAsync(string? description)
    {
        var scope = BrainScopeHelper.GetActiveScope();
        var catalog = GrainFactory.GetGrain<IBrainCatalog>(scope);
        var entries = await catalog.ListRegisteredAsync();

        var encryptedStates = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.IsInterpreted)
            {
                var neuronKey = BrainScopeHelper.GetScopedNeuronKey(scope, entry.TypeFullName);
                var memoryGrain = GrainFactory.GetGrain<INeuronMemoryGrain>(neuronKey);
                var bytes = await memoryGrain.GetEncryptedMemoryAsync();
                if (bytes is { Length: > 0 })
                {
                    encryptedStates[neuronKey] = bytes;
                }
            }
        }

        var checkpointId = Guid.NewGuid();
        var checkpoint = new BrainCheckpoint
        {
            Id = checkpointId,
            Timestamp = DateTimeOffset.UtcNow,
            Description = description,
            EncryptedNeuronStates = encryptedStates
        };

        var storeGrain = GrainFactory.GetGrain<IBrainCheckpointStoreGrain>(checkpointId);
        await storeGrain.SaveCheckpointAsync(checkpoint);

        return checkpointId;
    }

    public async Task RestoreCheckpointAsync(Guid checkpointId)
    {
        var storeGrain = GrainFactory.GetGrain<IBrainCheckpointStoreGrain>(checkpointId);
        var checkpoint = await storeGrain.GetCheckpointAsync();
        if (checkpoint is null)
        {
            throw new KeyNotFoundException($"Checkpoint with ID '{checkpointId}' not found.");
        }

        foreach (var kvp in checkpoint.EncryptedNeuronStates)
        {
            var memoryGrain = GrainFactory.GetGrain<INeuronMemoryGrain>(kvp.Key);
            await memoryGrain.SaveEncryptedMemoryAsync(kvp.Value);
        }
    }
}
