namespace DigitalBrain.Flutter.Workspace;

[GenerateSerializer, Alias("ui.workspace-window")]
public sealed record WorkspaceWindow(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] WindowReference Reference,
    [property: Id(3)] bool IsOpen);
