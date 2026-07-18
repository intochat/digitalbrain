using DigitalBrain.SDK.DigitalBrain.Ai.AiHealth;

namespace DigitalBrain.Kernel.Gateway;

public sealed class AiHealthProbe(IGrainFactory grains) : IAiHealthProbe
{
    public Task<AiHealthStatus> InspectAsync()
        => grains.GetGrain<IAiHealthNeuron>(Guid.Empty).InspectAsync();
}
