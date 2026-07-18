using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record ConnectGmailRequest([property: Id(1)] string UserAccountId
) : Synapse;
