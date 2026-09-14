namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-building-body")]
public sealed record SlotBuildingBody(
    [property: Id(0)] string? Generation,
    [property: Id(1)] IReadOnlyList<string> ChangedFiles);
