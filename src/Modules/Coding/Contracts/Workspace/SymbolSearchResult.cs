namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.symbol-search-result")]
public sealed record SymbolSearchResult(
    [property: Id(0)] IReadOnlyList<SymbolHit> Items,
    [property: Id(1)] int TotalCount,
    [property: Id(2)] bool Truncated);