namespace DigitalBrain.Aspire.Models.Xai;

using DigitalBrain.Core.Models;

// xAI ships new Grok ids frequently (variants like grok-4-1-fast-reasoning also exist) — re-check
// https://docs.x.ai before depending on this id for anything production-critical.
public sealed class Grok41 : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Xai;
    public override string Id => "grok-4-1-fast";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
