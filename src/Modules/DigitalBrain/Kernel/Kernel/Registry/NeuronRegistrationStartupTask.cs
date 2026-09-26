using DigitalBrain.Contracts.Registry;

namespace DigitalBrain.Core.Registry;

public sealed class NeuronRegistrationStartupTask(IGrainFactory grains, NeuronRegistrySnapshot snapshot) : IStartupTask
{
    public Task Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return grains.GetGrain<INeuronRegistryGrain>(snapshot.Version).ReplaceSnapshot(snapshot);
    }
}
