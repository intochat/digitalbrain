namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.symbol-hit")]
public sealed record SymbolHit(
    [property: Id(0)] string Id,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Name,
    [property: Id(3)] string Display,
    [property: Id(4)] string Project,
    [property: Id(5)] string Path,
    [property: Id(6)] int Line);