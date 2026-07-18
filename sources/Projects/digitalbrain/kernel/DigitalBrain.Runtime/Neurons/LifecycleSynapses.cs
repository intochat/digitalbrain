namespace DigitalBrain.Runtime.Neurons;

using Orleans;

[GenerateSerializer]
public sealed record NeuronActivated(
    [property: Id(0)] string NeuronType,
    [property: Id(1)] string InstanceId
) : Synapse;

[GenerateSerializer]
public sealed record NeuronDeactivated(
    [property: Id(0)] string NeuronType,
    [property: Id(1)] string InstanceId
) : Synapse;

[GenerateSerializer]
public sealed record NeuronUnresolvedReference(
    [property: Id(0)] string NeuronType,
    [property: Id(1)] string TargetReference
) : Synapse;
