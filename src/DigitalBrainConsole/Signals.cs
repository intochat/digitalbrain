using DigitalBrain.Abstractions.Signals;

namespace DigitalBrainConsole;

[GenerateSerializer]
[Alias("db.console.user-message-received")]
public sealed record UserMessageReceived(string Text) : Signal;
