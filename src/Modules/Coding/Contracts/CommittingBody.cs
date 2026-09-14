namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.committing-body")]
public sealed record CommittingBody([property: Id(0)] string Message);
