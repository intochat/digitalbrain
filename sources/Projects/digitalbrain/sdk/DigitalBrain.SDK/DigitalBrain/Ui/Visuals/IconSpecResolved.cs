using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record IconSpecResolved([property: Id(1)] string NeuronFqn,
    [property: Id(2)] uint Seed,
    [property: Id(3)] string Tone,
    [property: Id(4)] string ShapeHint,
    [property: Id(5)] string? OverrideAssetKey
) : Synapse;
