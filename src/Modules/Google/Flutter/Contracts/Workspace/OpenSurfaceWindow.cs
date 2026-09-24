namespace DigitalBrain.Flutter.Workspace;

[GenerateSerializer, Alias("ui.open-surface-window")]
public sealed record OpenSurfaceWindow(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string WindowId,
    [property: Id(2)] string Title,
    [property: Id(3)] WindowReference Reference,
    [property: Id(4)] long ExpectedRevision);
