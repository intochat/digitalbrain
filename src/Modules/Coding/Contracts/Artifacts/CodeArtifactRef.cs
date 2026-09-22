namespace DigitalBrain.Coding;

[GenerateSerializer, Alias("coding.artifact-ref")]
public sealed record CodeArtifactRef(
    [property: Id(0)] string Id,
    [property: Id(1)] string SourceHash,
    [property: Id(2)] string EnvironmentHash);
