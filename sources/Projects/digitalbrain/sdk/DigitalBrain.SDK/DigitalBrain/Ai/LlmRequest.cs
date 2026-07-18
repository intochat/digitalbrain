using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record LlmRequest([property: Id(1)] string? System,
    [property: Id(2)] IReadOnlyList<LlmMessage> Messages,
    [property: Id(3)] float? Temperature,
    [property: Id(4)] int? MaxOutputTokens
) : Synapse;

[GenerateSerializer]
public sealed record LlmMessage(
    [property: Id(0)] string Role,
    [property: Id(1)] string Content);
