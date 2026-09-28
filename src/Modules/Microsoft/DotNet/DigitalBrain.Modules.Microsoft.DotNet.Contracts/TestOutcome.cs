namespace DigitalBrain.Microsoft.DotNet;

[GenerateSerializer, Alias("microsoft.dotnet.test-outcome")]
public sealed record TestOutcome(
    [property: Id(0)] bool Succeeded,
    [property: Id(1)] int Total,
    [property: Id(2)] int Passed,
    [property: Id(3)] int Failed,
    [property: Id(4)] int Skipped,
    [property: Id(5)] IReadOnlyList<TestFailure> Failures,
    [property: Id(6)] double DurationSeconds,
    [property: Id(7)] string Invocation,
    [property: Id(8)] string? Detail);