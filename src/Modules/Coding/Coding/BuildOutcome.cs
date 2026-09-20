namespace DigitalBrain.Coding;

public sealed record BuildOutcome(bool Succeeded, IReadOnlyList<DiagnosticHit> Errors, int WarningCount, double DurationSeconds, string Invocation, string? Detail);
