using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Chat;

/// <summary>Requests a chat turn by its scheduled work id.</summary>
[GenerateSerializer]
[Alias("chat.read-turn")]
public sealed record ReadTurn([property: Id(0)] SignalId Turn);
