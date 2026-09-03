using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrainConsole;

[Alias("health")]
public interface IHealth : INeuron
{
    [Alias(nameof(Verify))]
    Task<bool> Verify(CancellationToken cancellationToken = default);
}
