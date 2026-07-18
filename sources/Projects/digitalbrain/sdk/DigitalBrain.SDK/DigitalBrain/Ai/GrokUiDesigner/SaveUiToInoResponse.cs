using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai.GrokUiDesigner;

[GenerateSerializer]
public sealed record SaveUiToInoResponse(
    [property: Id(0)] bool Success,
    [property: Id(1)] string? ErrorMessage = null
) : Synapse;
