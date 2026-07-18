using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Ui;

[GenerateSerializer]
public sealed record RenderDefaultSurfaceRequest(
    [property: Id(1)] string NeuronId
) : Synapse;
