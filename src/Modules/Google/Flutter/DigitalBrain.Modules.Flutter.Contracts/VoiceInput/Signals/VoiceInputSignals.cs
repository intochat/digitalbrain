using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.VoiceInput.Signals;

[GenerateSerializer, Alias("ui.voiceinput-changed")]
public sealed record VoiceInputChanged([property: Id(0)] string Name, [property: Id(1)] long Revision) : Signal;

[GenerateSerializer, Alias("ui.voice-captured")]
public sealed record VoiceCaptured([property: Id(0)] string Name, [property: Id(1)] byte[] Audio, [property: Id(2)] string MimeType) : Signal;
