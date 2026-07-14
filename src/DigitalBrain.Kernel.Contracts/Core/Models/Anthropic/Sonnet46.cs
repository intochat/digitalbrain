namespace DigitalBrain.Kernel.Contracts.Models.Anthropic;

public sealed class Sonnet46 : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Anthropic;
    public override string Id => "claude-sonnet-4-6";
    public override string DisplayName => "Claude Sonnet 4.6";
}
