using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal sealed class AnthropicProviderFactory : ApiKeyProviderFactory
{
    public override AiProvider Provider => AiProvider.Anthropic;

    public override IChatClient CreateChatClient(string model, AIOptions configuration)
        => new AnthropicClient
        {
            ApiKey = RequireApiKey(configuration, model, "WithLlm"),
            BaseUrl = configuration.Anthropic.Endpoint ?? "https://api.anthropic.com",
            Timeout = TimeSpan.FromMinutes(5),
        }.AsIChatClient(model);

    public override IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        AIOptions configuration)
        => throw new NotSupportedException("Anthropic does not provide embedding models.");
}