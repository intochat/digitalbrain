namespace DigitalBrain.Core.Models.Voice;

public sealed class Whisper1Local : VoiceToTextModel
{
    public override string Provider => DigitalBrainProviderIds.OpenAICompatible;
    public override string Id => "whisper-1";
    public override string DisplayName => "Local Whisper";
}
