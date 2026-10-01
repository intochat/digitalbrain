namespace DigitalBrain.AI;

internal sealed class OpenAIProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.OpenAI;
}
