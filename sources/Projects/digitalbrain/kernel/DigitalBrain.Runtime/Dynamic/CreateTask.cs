using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record CreateTask([property: Id(1)] string TaskDescription,
    [property: Id(2)] string Status
) : Synapse;
