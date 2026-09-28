namespace DigitalBrain.Microsoft.Roslyn;

[GenerateSerializer]
[Alias("coding.diagnostics-result")]
public sealed record DiagnosticsResult(
    [property: Id(0)] IReadOnlyList<DiagnosticHit> Items,
    [property: Id(1)] int ErrorCount,
    [property: Id(2)] int WarningCount,
    [property: Id(3)] bool Truncated,
    [property: Id(4)] int TotalCount);