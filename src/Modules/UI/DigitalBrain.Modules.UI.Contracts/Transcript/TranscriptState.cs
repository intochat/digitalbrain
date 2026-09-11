namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.transcript-state")]
public sealed record TranscriptState(
    [property: Id(0)] IReadOnlyList<TranscriptEntry> Entries);
