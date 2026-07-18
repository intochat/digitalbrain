using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai.GrokUiDesigner;

[GenerateSerializer]
public sealed record GrokUiDesignRequest([property: Id(0)] string Prompt) : Synapse;
