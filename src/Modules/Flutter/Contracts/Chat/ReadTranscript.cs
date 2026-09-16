namespace DigitalBrain.Chat;

/// <summary>Requests the chat transcript with an optional turn limit.</summary>
[GenerateSerializer]
[Alias("chat.read-transcript")]
public sealed record ReadTranscript([property: Id(0)] int? MaxTurns = null);
