using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.user-messaged")]
public sealed record UserMessaged(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Chat,
    [property: Id(2)] string Text,
    [property: Id(3)] ActorContext? Actor = null) : Signal;
