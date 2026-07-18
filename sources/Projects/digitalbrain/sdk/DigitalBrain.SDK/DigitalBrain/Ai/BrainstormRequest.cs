using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record BrainstormRequest([property: Id(1)] string Prompt,
    [property: Id(2)] int MinOptions,
    [property: Id(3)] int MaxOptions
) : Synapse;
