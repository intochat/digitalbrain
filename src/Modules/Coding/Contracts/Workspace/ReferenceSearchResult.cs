namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.reference-search-result")]
public sealed record ReferenceSearchResult(
    [property: Id(0)] string SymbolId,
    [property: Id(1)] IReadOnlyList<ReferenceHit> Items,
    [property: Id(2)] int TotalCount,
    [property: Id(3)] bool Truncated);