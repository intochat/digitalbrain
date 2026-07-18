using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record LayoutStateTransition([property: Id(1)] string ActiveLayout,
    [property: Id(2)] string? DataJson
) : Synapse;
