namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.graph-edge")]
public sealed record GraphEdgeState(
    [property: Id(0)] string Id,
    [property: Id(1)] string SourceId,
    [property: Id(2)] string TargetId,
    [property: Id(3)] bool Dotted = false);
