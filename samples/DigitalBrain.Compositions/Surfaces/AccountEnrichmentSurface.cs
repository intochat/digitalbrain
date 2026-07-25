using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;

namespace DigitalBrain.Surfaces;

public sealed class AccountEnrichmentSurface
{
    public const string SceneKey = "enrichment";
    public const string SceneTitle = "Account enrichment";

    public async Task RunAsync(
        IDigitalBrain brain,
        string shellName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        cancellationToken.ThrowIfCancellationRequested();

        var shell = brain.Get<IShell>(shellName);
        await shell.Open(new OpenScene(CommandId.New(), SceneKey, SceneTitle));
    }
}
