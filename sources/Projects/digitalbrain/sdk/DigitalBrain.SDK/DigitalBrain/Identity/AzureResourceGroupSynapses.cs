using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Identity;

[GenerateSerializer]
public sealed record Create([property: Id(1)] string ResourceGroupName,
    [property: Id(2)] string Location,
    [property: Id(3)] string SubscriptionId
) : Synapse;

[GenerateSerializer]
public sealed record Created([property: Id(1)] string ResourceGroupName,
    [property: Id(2)] string Location,
    [property: Id(3)] bool Success,
    [property: Id(4)] string ProvisioningState,
    [property: Id(5)] string ErrorMessage
) : Synapse;
