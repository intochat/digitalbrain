using DigitalBrain.Contracts;

namespace DigitalBrain.Coding;

[GenerateSerializer, Alias("coding.change-set-changed")]
public sealed record ChangeSetChanged(
    [property: Id(0)] string ChangeId,
    [property: Id(1)] ChangeSetStatus Status,
    [property: Id(2)] int EditCount,
    [property: Id(3)] int Revision) : Signal;