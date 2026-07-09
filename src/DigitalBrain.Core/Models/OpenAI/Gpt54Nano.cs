namespace DigitalBrain.Core.Models.OpenAI;

public sealed class Gpt54Nano : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.OpenAI;
    public override string Id => "gpt-5.4-nano";
    public override string DisplayName => "GPT-5.4 Nano";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
