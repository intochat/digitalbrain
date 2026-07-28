using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.user-messaged")]
public sealed record UserMessaged(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Chat,
    [property: Id(2)] string Text,
    [property: Id(3)] IReadOnlyList<ChatTurn> Transcript) : Synapse;
