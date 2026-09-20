namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.skeleton-query")]
public sealed record SkeletonQuery([property: Id(0)] string Path);
