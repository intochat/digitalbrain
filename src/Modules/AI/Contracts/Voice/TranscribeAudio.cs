using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.transcribe-audio")]
public sealed record TranscribeAudio(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ContentType,
    [property: Id(2)] string FileName,
    [property: Id(3)] byte[] Audio,
    [property: Id(4)] string? Language = null,
    [property: Id(5)] string? Intent = null) : RequestSynapse<Transcribed>;
