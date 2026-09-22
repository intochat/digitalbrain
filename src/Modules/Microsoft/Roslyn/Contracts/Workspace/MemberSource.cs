namespace DigitalBrain.Microsoft.Roslyn;

[GenerateSerializer]
[Alias("coding.member-source")]
public sealed record MemberSource(
    [property: Id(0)] string Id,
    [property: Id(1)] string Path,
    [property: Id(2)] int StartLine,
    [property: Id(3)] int EndLine,
    [property: Id(4)] string Source);