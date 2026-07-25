using DigitalBrain.Client;

namespace DigitalBrain.Shell;

public sealed class PostAuthBootstrap
{
    public Task RunAsync(
        IDigitalBrain brain,
        string shellName,
        CancellationToken cancellationToken)
        => new OpenHome().RunAsync(brain, shellName, cancellationToken);
}
