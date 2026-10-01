namespace DigitalBrain.AI;

internal sealed class GoogleProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.Google;
}
