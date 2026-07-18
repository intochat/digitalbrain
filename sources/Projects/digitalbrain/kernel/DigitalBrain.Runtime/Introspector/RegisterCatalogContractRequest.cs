using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record RegisterCatalogContractRequest([property: Id(1)] string Fqn,
    [property: Id(2)] CatalogContractKind Kind,
    [property: Id(3)] IReadOnlyList<string> Fields
) : Synapse;
