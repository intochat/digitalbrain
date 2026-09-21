namespace DigitalBrain.Coding;

public sealed record GitCommitOutcome(string Hash, string Branch, IReadOnlyList<string> Files);