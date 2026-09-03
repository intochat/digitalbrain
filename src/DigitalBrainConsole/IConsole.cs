using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrainConsole;

[Alias("console")]
public interface IConsole : INeuron
{
    [Alias(nameof(Attach))]
    Task Attach(CancellationToken cancellationToken = default);
}
