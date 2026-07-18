using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record GmailConnected([property: Id(1)] string UserAccountId
) : Synapse;
