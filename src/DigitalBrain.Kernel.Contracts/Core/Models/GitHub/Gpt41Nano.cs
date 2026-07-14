namespace DigitalBrain.Kernel.Contracts.Models.GitHub;

public sealed class Gpt41Nano : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.GitHubModels;
    public override string Id => "openai/gpt-4.1-nano";
    public override string DisplayName => "GPT-4.1 Nano";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
