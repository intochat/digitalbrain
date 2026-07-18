using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai.GrokUiDesigner;

[GenerateSerializer]
public sealed record GrokUiDesignResponse(
    [property: Id(0)] string UiJson,
    [property: Id(1)] string Explanation,
    [property: Id(2)] string InoCode
) : Synapse;
