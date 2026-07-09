namespace DigitalBrain.Core.Models.GitHub;

public sealed class Gpt41Mini : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.GitHubModels;
    public override string Id => "openai/gpt-4.1-mini";
    public override string DisplayName => "GPT-4.1 Mini";
}
