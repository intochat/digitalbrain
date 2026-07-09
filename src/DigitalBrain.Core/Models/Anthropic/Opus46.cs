namespace DigitalBrain.Core.Models.Anthropic;

public sealed class Opus46 : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Anthropic;
    public override string Id => "claude-opus-4-6";
    public override string DisplayName => "Claude Opus 4.6";
}
