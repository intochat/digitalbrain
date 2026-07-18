namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record IconOverride(
    [property: Id(0)] string NeuronFqn,
    [property: Id(1)] string? Tone,
    [property: Id(2)] string? ShapeHint,
    [property: Id(3)] string? OverrideAssetKey,
    [property: Id(4)] DateTimeOffset UpdatedUtc);
