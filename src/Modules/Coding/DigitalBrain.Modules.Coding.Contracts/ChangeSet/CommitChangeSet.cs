namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.commit-change-set")]
public sealed record CommitChangeSet([property: Id(0)] string Message);