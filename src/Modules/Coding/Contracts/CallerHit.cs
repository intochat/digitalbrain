namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.caller-hit")]
public sealed record CallerHit(
    [property: Id(0)] string Id,
    [property: Id(1)] string Display,
    [property: Id(2)] string Path,
    [property: Id(3)] int Line,
    [property: Id(4)] string Project);
