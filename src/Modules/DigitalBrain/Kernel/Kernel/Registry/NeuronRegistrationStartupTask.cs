using DigitalBrain.Contracts.Registry;

namespace DigitalBrain.Core.Registry;

public sealed class NeuronRegistrationStartupTask(IGrainFactory grains, INeuronRegistry registry) : IStartupTask
{
    public Task Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return grains.GetGrain<INeuronRegistryGrain>(registry.Version).Register(NeuronRegistry.ToRegistrations(registry));
    }
}
