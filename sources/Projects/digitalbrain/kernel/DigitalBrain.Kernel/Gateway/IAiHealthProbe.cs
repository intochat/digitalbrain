using DigitalBrain.SDK.DigitalBrain.Ai.AiHealth;

namespace DigitalBrain.Kernel.Gateway;

public interface IAiHealthProbe
{
    Task<AiHealthStatus> InspectAsync();
}
