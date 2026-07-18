namespace DigitalBrain.SDK.DigitalBrain.Ai.AiHealth;

public sealed class AiHealthNeuron(IAiHealthLogic logic) : Grain, IAiHealthNeuron
{
    public Task<AiHealthStatus> InspectAsync() => Task.FromResult(logic.Inspect());
}
