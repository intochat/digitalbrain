namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.change-set-receipt")]
public sealed record ChangeSetReceipt(
    [property: Id(0)] string ChangeId,
    [property: Id(1)] int EditCount,
    [property: Id(2)] ChangeSetStatus Status);