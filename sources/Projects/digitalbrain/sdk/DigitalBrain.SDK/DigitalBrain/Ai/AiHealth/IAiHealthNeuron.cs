namespace DigitalBrain.SDK.DigitalBrain.Ai.AiHealth;

public interface IAiHealthNeuron : IGrainWithGuidKey
{
    Task<AiHealthStatus> InspectAsync();
}
