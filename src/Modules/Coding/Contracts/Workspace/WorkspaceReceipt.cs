namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.workspace-receipt")]
public sealed record WorkspaceReceipt([property: Id(0)] string Key, [property: Id(1)] long Generation);
