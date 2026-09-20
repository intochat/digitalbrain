using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Workspace.Signals;

[GenerateSerializer, Alias("ui.workspace-changed")]
public sealed record WorkspaceChanged(
    [property: Id(0)] string WorkspaceId,
    [property: Id(1)] long Revision) : Signal;
