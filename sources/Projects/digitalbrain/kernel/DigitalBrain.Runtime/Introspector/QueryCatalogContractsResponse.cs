using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

// v5 C2 collapsed Signal into Synapse. The Signal ordinal stays for Orleans
// wire compatibility with prior cluster state, but new code never emits it —
// IntrospectorNeuron maps ContractKind.Synapse → CatalogContractKind.Synapse.
[GenerateSerializer]
public enum CatalogContractKind
{
    Synapse = 0,
    [Obsolete("Use CatalogContractKind.Synapse — Signal collapsed in v5 C2.")]
    Signal = 1,
    Neuron = 2
}

[GenerateSerializer]
public sealed record CatalogContractSchema(
    [property: Id(0)] string Fqn,
    [property: Id(1)] CatalogContractKind Kind,
    [property: Id(2)] IReadOnlyList<string> Fields);

[GenerateSerializer]
public sealed record QueryCatalogContractsResponse([property: Id(1)] IReadOnlyList<CatalogContractSchema> Schemas
) : Synapse;
