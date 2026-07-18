using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record LlmResponse([property: Id(1)] string Text,
    [property: Id(2)] string? FinishReason,
    [property: Id(3)] long? InputTokens,
    [property: Id(4)] long? OutputTokens
) : Synapse;
