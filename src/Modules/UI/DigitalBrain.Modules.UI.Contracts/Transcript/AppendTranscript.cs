using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Appends an entry to the bounded transcript.</summary>
[GenerateSerializer]
[Alias("ui.append-transcript")]
public sealed record AppendTranscript(
    CommandId Id,
    [property: Id(0)] TranscriptEntry Entry) : Command(Id);
