namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.reference-search")]
public sealed record ReferenceSearch([property: Id(0)] string SymbolId, [property: Id(1)] int Limit = 50);
