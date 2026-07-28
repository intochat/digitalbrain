using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.assistant-answered")]
public sealed record AssistantAnswered([property: Id(0)] CommandId CommandId, [property: Id(1)] string Text) : Synapse;
