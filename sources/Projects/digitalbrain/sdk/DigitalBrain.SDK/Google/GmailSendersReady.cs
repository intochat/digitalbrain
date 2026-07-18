using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record GmailSendersReady([property: Id(1)] string UserAccountId,
    [property: Id(2)] IReadOnlyList<GmailSender> Senders
) : Synapse;
