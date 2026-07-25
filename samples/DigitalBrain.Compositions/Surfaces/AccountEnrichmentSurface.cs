using DigitalBrain.Client;

namespace DigitalBrain.Surfaces;

public sealed class AccountEnrichmentSurface
{
    public const string SceneKey = "enrichment";
    public const string SceneTitle = "Account enrichment";

    public Task RunAsync(
        IDigitalBrain brain,
        string shellName,
        CancellationToken cancellationToken)
        => new Shell.OpenHome().RunAsync(
            brain,
            shellName,
            sceneKey: SceneKey,
            title: SceneTitle,
            cancellationToken);
}
