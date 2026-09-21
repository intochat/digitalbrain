namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.derived-query")]
public sealed record DerivedQuery([property: Id(0)] string SymbolId, [property: Id(1)] int Limit = 50);