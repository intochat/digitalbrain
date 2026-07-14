namespace DigitalBrain.Kernel.Contracts.Models.OpenAI;

public sealed class Gpt54 : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.OpenAI;
    public override string Id => "gpt-5.4";
    public override string DisplayName => "GPT-5.4";
}
