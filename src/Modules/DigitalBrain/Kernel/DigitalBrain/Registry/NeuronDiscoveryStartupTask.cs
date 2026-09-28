namespace DigitalBrain.Core.Registry;

public sealed class NeuronDiscoveryStartupTask(NeuronRegistry registry) : IStartupTask
{
    public Task Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        registry.Discover();
        return Task.CompletedTask;
    }
}
