using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record FindChainsByConversationTextRequest([property: Id(1)] string Text,
    [property: Id(2)] DateTimeOffset? Since,
    [property: Id(3)] DateTimeOffset? Until,
    [property: Id(4)] int Limit
) : Synapse;
