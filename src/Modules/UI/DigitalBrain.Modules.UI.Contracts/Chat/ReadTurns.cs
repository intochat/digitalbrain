namespace DigitalBrain.Chat;

/// <summary>Requests chat turn snapshots with an optional turn limit.</summary>
[GenerateSerializer]
[Alias("chat.read-turns")]
public sealed record ReadTurns([property: Id(0)] int? MaxTurns = null);
