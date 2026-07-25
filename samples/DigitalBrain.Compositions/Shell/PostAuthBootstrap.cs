using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;

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

        var shell = brain.Get<IShell>(shellName);
        await shell.Open(new OpenScene(CommandId.New(), OpenHome.SceneKey, OpenHome.SceneTitle));
    }
}
