namespace DigitalBrain.Aspire.Models.Anthropic;

using DigitalBrain.Core.Models;

// Fast/cheap Anthropic tier.
public sealed class Claude45Haiku : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Anthropic;
    public override string Id => "claude-haiku-4-5-20251001";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
