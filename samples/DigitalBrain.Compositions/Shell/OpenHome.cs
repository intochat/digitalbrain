using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;

namespace DigitalBrain.Shell;

public sealed class OpenHome
{
    public async Task RunAsync(
        IDigitalBrain brain,
        string shellName,
        string sceneKey,
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        cancellationToken.ThrowIfCancellationRequested();

        var shell = brain.Get<IShell>(shellName);
        await shell.Open(new OpenScene(CommandId.New(), sceneKey, title));
    }
}
