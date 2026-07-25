using DigitalBrain.Client;

namespace DigitalBrain.Shell;

public sealed class PostAuthBootstrap
{
    public async Task RunAsync(
        IDigitalBrain brain,
        string shellName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        cancellationToken.ThrowIfCancellationRequested();

        await new OpenHome().RunAsync(
            brain,
            shellName,
            sceneKey: "home",
            title: "Home",
            cancellationToken);
    }
}
