namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.symbol-search")]
public sealed record SymbolSearch([property: Id(0)] string Query, [property: Id(1)] int Limit = 20);
