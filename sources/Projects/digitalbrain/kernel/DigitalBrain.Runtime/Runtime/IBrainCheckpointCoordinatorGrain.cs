namespace DigitalBrain.Runtime.Runtime;

public interface IBrainCheckpointCoordinatorGrain : IGrainWithGuidKey
{
    Task<Guid> CreateCheckpointAsync(string? description);
    Task RestoreCheckpointAsync(Guid checkpointId);
}
