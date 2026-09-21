namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.project-node")]
public sealed record ProjectNode(
    [property: Id(0)] string Name,
    [property: Id(1)] string Path,
    [property: Id(2)] string Cluster,
    [property: Id(3)] int DocumentCount);