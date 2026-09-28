namespace DigitalBrain.Flutter.Workspace;

// The idempotent receipt of one open request: the current workspace state plus the revision this
// operation originally applied. A replay after close returns the same applied revision without
// reopening the window, so an operation can rebuild its result from this receipt alone.
[GenerateSerializer, Alias("ui.workspace-open-result")]
public sealed record WorkspaceOpenResult(
    [property: Id(0)] WorkspaceState State,
    [property: Id(1)] long AppliedRevision);