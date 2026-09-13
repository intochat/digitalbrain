namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.diagnostics-result")]
public sealed record DiagnosticsResult(
    [property: Id(0)] IReadOnlyList<DiagnosticHit> Items,
    [property: Id(1)] int ErrorCount,
    [property: Id(2)] int WarningCount,
    [property: Id(3)] bool Truncated);
