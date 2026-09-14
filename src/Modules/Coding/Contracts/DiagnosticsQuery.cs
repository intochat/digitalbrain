namespace DigitalBrain.Coding;

// Exactly one of Path or Project names the scope; both null means the whole solution.
[GenerateSerializer]
[Alias("coding.diagnostics-query")]
public sealed record DiagnosticsQuery(
    [property: Id(0)] string? Path = null,
    [property: Id(1)] string? Project = null,
    [property: Id(2)] int Limit = 50);
