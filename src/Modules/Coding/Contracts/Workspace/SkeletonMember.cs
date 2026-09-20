namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.skeleton-member")]
public sealed record SkeletonMember(
    [property: Id(0)] string Id,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Signature,
    [property: Id(3)] int Line,
    [property: Id(4)] int Depth);
