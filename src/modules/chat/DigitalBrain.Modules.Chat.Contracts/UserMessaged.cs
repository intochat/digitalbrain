using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.user-messaged")]
[Description("User message accepted into a chat transcript")]
public sealed record UserMessaged(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Chat,
    [property: Id(2)] string Text) : Synapse;
