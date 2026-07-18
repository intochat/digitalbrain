using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record GetLastNGmailSendersRequest([property: Id(1)] string UserAccountId,
    [property: Id(2)] int N
) : Synapse;
