namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.diagnostic-hit")]
public sealed record DiagnosticHit(
    [property: Id(0)] string Id,
    [property: Id(1)] string Severity,
    [property: Id(2)] string Message,
    [property: Id(3)] string Path,
    [property: Id(4)] int Line);
