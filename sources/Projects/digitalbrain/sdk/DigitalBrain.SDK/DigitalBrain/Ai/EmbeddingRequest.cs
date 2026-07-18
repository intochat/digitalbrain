using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record EmbeddingRequest([property: Id(1)] IReadOnlyList<string> Texts
) : Synapse;
