using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai.GrokUiDesigner;

[GenerateSerializer]
public sealed record SaveUiToInoRequest(
    [property: Id(0)] string InoCode,
    [property: Id(1)] string Filename
) : Synapse;
