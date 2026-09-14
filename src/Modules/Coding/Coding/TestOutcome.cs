namespace DigitalBrain.Coding;

public sealed record TestOutcome(bool Succeeded, int Total, int Passed, int Failed, int Skipped, IReadOnlyList<TestFailure> Failures, double DurationSeconds, string Command, string? Detail);
