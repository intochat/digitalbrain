namespace DigitalBrain.Aspire.Models.Anthropic;

using DigitalBrain.Core.Models;

// Balanced/reasoning-tier Anthropic model.
public sealed class ClaudeSonnet5 : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Anthropic;
    public override string Id => "claude-sonnet-5";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
