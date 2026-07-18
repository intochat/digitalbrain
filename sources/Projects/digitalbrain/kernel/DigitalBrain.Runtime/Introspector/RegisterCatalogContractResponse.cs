using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record RegisterCatalogContractResponse([property: Id(1)] bool Success,
    [property: Id(2)] string Message
) : Synapse;
