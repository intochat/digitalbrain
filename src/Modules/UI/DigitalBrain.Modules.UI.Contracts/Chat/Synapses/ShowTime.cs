using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.show-time")]
public sealed record ShowTime([property: Id(0)] CommandId OfferCommandId) : Synapse;
