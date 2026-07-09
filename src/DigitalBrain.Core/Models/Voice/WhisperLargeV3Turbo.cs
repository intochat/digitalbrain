namespace DigitalBrain.Core.Models.Voice;

public sealed class WhisperLargeV3Turbo : VoiceToTextModel
{
    public override string Provider => DigitalBrainProviderIds.OpenAICompatible;
    public override string Id => "whisper-large-v3-turbo";
    public override string DisplayName => "Whisper Large V3 Turbo";
}
