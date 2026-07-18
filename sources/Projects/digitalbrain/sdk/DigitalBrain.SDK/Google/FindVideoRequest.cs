using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record FindVideoRequest([property: Id(1)] string UserAccountId,
    [property: Id(2)] string Query
) : Synapse;
