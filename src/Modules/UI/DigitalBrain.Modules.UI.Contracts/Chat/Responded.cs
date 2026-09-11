using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.responded")]
public sealed record Responded(
    [property: Id(0)] SignalId Turn,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] NeuronId Chat,
    [property: Id(3)] string Text,
    [property: Id(4)] string Author = "",
    [property: Id(5)] IReadOnlyList<UiCardOffer>? Cards = null);
