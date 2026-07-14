namespace DigitalBrain.Kernel.Contracts.Models.Anthropic;

public sealed class Claude45Haiku : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Anthropic;
    public override string Id => "claude-haiku-4-5-20251001";
    public override string DisplayName => "Claude 4.5 Haiku";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
