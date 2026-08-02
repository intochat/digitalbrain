using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Shell;

namespace DigitalBrain.Compositions;

public sealed class OpenHome
{
    public const string SceneKey = "home";
    public const string SceneTitle = "Home";

    public async Task RunAsync(IDigitalBrain brain, string shellName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        cancellationToken.ThrowIfCancellationRequested();

        var shell = brain.GetGrainProxy<IShell>(shellName);
        await shell.Open(new OpenScene(CommandId.New(), SceneKey, SceneTitle));
    }
}
