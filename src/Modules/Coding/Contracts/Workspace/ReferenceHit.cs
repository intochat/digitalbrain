namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.reference-hit")]
public sealed record ReferenceHit(
    [property: Id(0)] string Path,
    [property: Id(1)] int Line,
    [property: Id(2)] string Project,
    [property: Id(3)] string Text,
    [property: Id(4)] bool Generated);
