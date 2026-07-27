using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.assistant-responded")]
public sealed record AssistantResponded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Chat,
    [property: Id(2)] string Text) : Synapse;
