namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.implementations-query")]
public sealed record ImplementationsQuery([property: Id(0)] string SymbolId, [property: Id(1)] int Limit = 50);