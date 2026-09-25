using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Graph.Signals;

[GenerateSerializer, Alias("ui.graph-activity-observed")]
public sealed record GraphActivityObserved(
    [property: Id(0)] string Name,
    [property: Id(1)] string EventId,
    [property: Id(2)] string SourceId,
    [property: Id(3)] string? TargetId,
    [property: Id(4)] string Kind,
    [property: Id(5)] string Status,
    [property: Id(6)] long Sequence) : Signal;
