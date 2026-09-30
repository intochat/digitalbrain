namespace DigitalBrain.AI;

internal sealed class XAIProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.XAI;
}
