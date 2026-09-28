namespace DigitalBrain.Microsoft.Roslyn;

[GenerateSerializer]
[Alias("coding.callers-result")]
public sealed record CallersResult(
    [property: Id(0)] string SymbolId,
    [property: Id(1)] IReadOnlyList<CallerHit> Items,
    [property: Id(2)] int TotalCount,
    [property: Id(3)] bool Truncated);