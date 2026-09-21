namespace DigitalBrain.AI.OpenAI;

public sealed class Whisper1 : TranscriptionModel<IWhisper1>
{
    public override string Id => "whisper-1";

    public override AiProvider Provider => AiProvider.OpenAI;

    // The only hosted model offering timestamps and subtitle formats.
    public override TranscriptionFormats Formats =>
        TranscriptionFormats.Text
        | TranscriptionFormats.Json
        | TranscriptionFormats.VerboseJson
        | TranscriptionFormats.Srt
        | TranscriptionFormats.Vtt;
}

public interface IWhisper1 : ITranscription;