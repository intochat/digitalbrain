using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Telegram;

[GenerateSerializer]
public sealed record SendTelegramAlertRequest([property: Id(1)] string ChatId,
    [property: Id(2)] string Message
) : Synapse;
