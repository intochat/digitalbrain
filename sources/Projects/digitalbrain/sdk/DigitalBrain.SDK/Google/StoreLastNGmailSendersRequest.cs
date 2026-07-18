using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record StoreLastNGmailSendersRequest([property: Id(1)] string UserAccountId,
    [property: Id(2)] int N,
    [property: Id(3)] string DatabaseId
) : Synapse;
