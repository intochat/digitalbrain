namespace DigitalBrain.Kernel.Contracts.Models.GitHub;

public sealed class O4Mini : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.GitHubModels;
    public override string Id => "openai/o4-mini";
    public override string DisplayName => "o4-mini";
}
