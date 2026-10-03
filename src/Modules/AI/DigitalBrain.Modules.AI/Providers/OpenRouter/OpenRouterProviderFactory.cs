namespace DigitalBrain.AI;

internal sealed class OpenRouterProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.OpenRouter;
}
