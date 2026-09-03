using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrainConsole;

[Alias("aspire")]
public interface IAspire : INeuron
{
    [Alias(nameof(StartDistributedApp))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task StartDistributedApp(string? appHostProject = null, CancellationToken cancellationToken = default);
}
