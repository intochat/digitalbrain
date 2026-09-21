namespace DigitalBrain.Flutter.Workspace;

[GenerateSerializer, Alias("ui.workspace-state")]
public sealed record WorkspaceState(
    [property: Id(0)] long Revision,
    [property: Id(1)] IReadOnlyList<WorkspaceWindow> Windows);