namespace DigitalBrain.Runtime.Runtime;

public interface IBrainCheckpointStoreGrain : IGrainWithGuidKey
{
    Task SaveCheckpointAsync(BrainCheckpoint checkpoint);
    Task<BrainCheckpoint?> GetCheckpointAsync();
}
