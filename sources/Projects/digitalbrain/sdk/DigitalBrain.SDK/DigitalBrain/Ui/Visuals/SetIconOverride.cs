using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record SetIconOverride([property: Id(1)] string NeuronFqn,
    [property: Id(2)] string? Tone,
    [property: Id(3)] string? ShapeHint,
    [property: Id(4)] string? OverrideAssetKey
) : Synapse;
