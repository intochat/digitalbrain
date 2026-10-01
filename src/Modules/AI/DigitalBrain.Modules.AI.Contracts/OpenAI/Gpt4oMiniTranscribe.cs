namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt4oMiniTranscribe : TranscriptionModel<IGpt4oMiniTranscribe>
{
    public override string Id => "gpt-4o-mini-transcribe";

    public override AiProvider Provider => AiProvider.OpenAI;

    // The gpt-4o transcribe models return text or json only.
    public override TranscriptionFormats Formats =>
        TranscriptionFormats.Text | TranscriptionFormats.Json;
}

public interface IGpt4oMiniTranscribe : ITranscription;
