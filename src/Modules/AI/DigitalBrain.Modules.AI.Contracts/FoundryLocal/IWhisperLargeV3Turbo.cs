namespace DigitalBrain.AI.FoundryLocal;

public sealed class WhisperLargeV3Turbo : TranscriptionModel<IWhisperLargeV3Turbo>
{
    public override string Id => "whisper-large-v3-turbo";

    public override AiProvider Provider => AiProvider.FoundryLocal;
}

// Marker for AppHost WithVoiceToText<IWhisperLargeV3Turbo>() model selection.
public interface IWhisperLargeV3Turbo : ITranscription;
