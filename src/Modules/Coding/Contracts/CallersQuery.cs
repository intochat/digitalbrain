namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.callers-query")]
public sealed record CallersQuery([property: Id(0)] string SymbolId, [property: Id(1)] int Limit = 50);
