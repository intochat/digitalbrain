namespace DigitalBrain.Coding;

public sealed record CommitOutcome(IReadOnlyList<string> WrittenPaths, long Generation);
