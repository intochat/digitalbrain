namespace DigitalBrain.Microsoft.Roslyn;

[GenerateSerializer]
[Alias("coding.skeleton")]
public sealed record Skeleton(
    [property: Id(0)] string Path,
    [property: Id(1)] string Project,
    [property: Id(2)] IReadOnlyList<SkeletonMember> Members);