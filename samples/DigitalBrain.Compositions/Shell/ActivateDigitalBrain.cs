using DigitalBrain.Client;

namespace DigitalBrain.Shell;

public sealed class ActivateDigitalBrain
{
    public Task RunAsync(
        IDigitalBrain brain,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brain);
        cancellationToken.ThrowIfCancellationRequested();

        return brain.ActivateAsync();
    }
}
