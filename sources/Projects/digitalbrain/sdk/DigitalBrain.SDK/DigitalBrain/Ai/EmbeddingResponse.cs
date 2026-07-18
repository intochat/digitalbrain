using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record EmbeddingResponse([property: Id(1)] IReadOnlyList<float[]> Vectors,
    [property: Id(2)] int Dimensions,
    [property: Id(3)] string ModelId
) : Synapse;
