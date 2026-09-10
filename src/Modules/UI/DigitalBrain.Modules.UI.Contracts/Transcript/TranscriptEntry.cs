using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.transcript-entry")]
public sealed record TranscriptEntry(
    [property: Id(0)] bool FromUser,
    [property: Id(1)] string Text,
    [property: Id(2)] string CommandId,
    [property: Id(3)] DateTimeOffset At);
