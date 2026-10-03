using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.VoiceInput;

// Captures audio from the user; whoever listens decides how to transcribe it.
[Alias("voiceinput"), Orleans.Metadata.DefaultGrainType(UIVocabulary.VoiceInputType)]
public interface IVoiceInput : INeuron
{
    const int MaxAudioBytes = 8 * 1024 * 1024;

    Task Configure(string label);
    Task Capture(byte[] audio, string mimeType);
    [ReadOnly, Alias("read")] Task<VoiceInputState> Read();
}

[GenerateSerializer, Alias("ui.voiceinput-state")]
public sealed class VoiceInputState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public long Revision { get; set; }
    [Id(2)] public string Label { get; set; } = "";
    [Id(3)] public int Captures { get; set; }
}
