namespace DigitalBrain.Kernel.Contracts.Models.AzureOpenAI;

public sealed class Gpt4oMini : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.AzureOpenAI;
    public override string Id => "gpt-4o-mini";
    public override string DisplayName => "GPT-4o mini";
}
