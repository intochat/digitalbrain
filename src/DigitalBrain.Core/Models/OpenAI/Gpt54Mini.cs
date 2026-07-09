namespace DigitalBrain.Core.Models.OpenAI;

public sealed class Gpt54Mini : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.OpenAI;
    public override string Id => "gpt-5.4-mini";
    public override string DisplayName => "GPT-5.4 Mini";
}
