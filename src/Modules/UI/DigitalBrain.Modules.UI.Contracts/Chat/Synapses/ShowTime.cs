using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.show-time")]
[Description("Ask a chat to answer with the current time")]
public sealed record ShowTime([property: Id(0)] CommandId OfferCommandId) : Synapse;
