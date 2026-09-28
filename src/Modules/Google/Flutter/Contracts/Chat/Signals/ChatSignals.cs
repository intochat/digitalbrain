using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Chat.Signals;

[GenerateSerializer, Alias("ui.chat-changed")]
public sealed record ChatChanged([property: Id(0)] string Name, [property: Id(1)] long Revision) : Signal;

[GenerateSerializer, Alias("ui.chat-submitted")]
public sealed record ChatSubmitted([property: Id(0)] string Name, [property: Id(1)] string Text) : Signal;
