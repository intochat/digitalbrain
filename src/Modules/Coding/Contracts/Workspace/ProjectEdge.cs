namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.project-edge")]
public sealed record ProjectEdge([property: Id(0)] string From, [property: Id(1)] string To);