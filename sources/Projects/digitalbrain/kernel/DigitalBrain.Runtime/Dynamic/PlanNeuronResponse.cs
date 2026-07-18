using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record PlanNeuronResponse([property: Id(1)] string FeatureText,
    [property: Id(2)] string StepsCode,
    [property: Id(3)] string ImplCode,
    [property: Id(4)] string DisplayName,
    [property: Id(5)] string Icon,
    [property: Id(6)] IReadOnlyList<string> RequiresCapabilities,
    [property: Id(7)] string InvocationSynapseType,
    [property: Id(8)] string InvocationPayloadJson,
    [property: Id(9)] string ResponseSynapseType
) : Synapse;
