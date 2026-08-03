using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Shell;

namespace DigitalBrain.Compositions;

public sealed class NavigateShell
{
    public async Task RunAsync(
        IDigitalBrain brain,
        string shellName,
        IReadOnlyList<(string SceneKey, string Title)> scenes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentNullException.ThrowIfNull(scenes);
        if (scenes.Count == 0)
        {
            throw new ArgumentException("NavigateShell requires at least one scene.", nameof(scenes));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var shell = brain.GetGrainProxy<IShell>(shellName);
        foreach (var (sceneKey, title) in scenes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sceneKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            cancellationToken.ThrowIfCancellationRequested();
            await shell.Open(new OpenScene(CommandId.New(), sceneKey, title));
        }
    }
}
