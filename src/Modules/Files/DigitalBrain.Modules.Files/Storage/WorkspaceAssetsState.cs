using Orleans;

namespace DigitalBrain.Files;

[GenerateSerializer, Alias("intochat.workspace-assets-state")]
public sealed class WorkspaceAssetsState
{
    [Id(0)] public Dictionary<string, WorkspaceAsset> Assets { get; set; } = [];
    [Id(1)] public HashSet<string> Pages { get; set; } = [];
}
