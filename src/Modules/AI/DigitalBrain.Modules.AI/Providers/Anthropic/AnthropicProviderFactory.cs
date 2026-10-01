using Anthropic;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class AnthropicProviderFactory : ApiKeyProviderFactory
{
    public override AiProvider Provider => AiProvider.Anthropic;

    public override IChatClient CreateChatClient(string model, AIOptions configuration, IAiCredentials credentials, string? endpoint = null)
        => new AnthropicClient
        {
            ApiKey = ReleaseApiKey(credentials),
            BaseUrl = EndpointOf(configuration, credentials, endpoint).OriginalString,
            Timeout = TimeSpan.FromMinutes(5),
        }.AsIChatClient(model);

    public override IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        AIOptions configuration,
        IAiCredentials credentials)
        => throw new NotSupportedException("Anthropic does not provide embedding models.");
}
