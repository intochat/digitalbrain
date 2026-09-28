namespace DigitalBrain.Microsoft.DotNet;

[GenerateSerializer, Alias("microsoft.dotnet.build-outcome")]
public sealed record BuildOutcome(
    [property: Id(0)] bool Succeeded,
    [property: Id(1)] IReadOnlyList<BuildDiagnostic> Errors,
    [property: Id(2)] int WarningCount,
    [property: Id(3)] double DurationSeconds,
    [property: Id(4)] string Invocation,
    [property: Id(5)] string? Detail);