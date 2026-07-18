using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record UiLayoutTransitionRequested([property: Id(1)] string LayoutName,
    [property: Id(2)] string? DataJson
) : Synapse;
