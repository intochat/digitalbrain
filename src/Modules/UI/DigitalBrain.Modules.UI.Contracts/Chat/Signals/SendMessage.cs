using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.send-message")]
public sealed record SendMessage(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Text,
    [property: Id(2)] ActorContext? Actor = null);
