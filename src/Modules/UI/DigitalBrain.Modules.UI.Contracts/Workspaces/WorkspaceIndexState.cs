namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.workspace-index-state")]
public sealed record WorkspaceIndexState(
    [property: Id(0)] IReadOnlyList<WorkspaceRecord> Workspaces);
