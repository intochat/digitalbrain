using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal interface ILlmProviderFactory
{
    AiProvider Provider { get; }

    bool IsConfigured(AIOptions configuration, IAiCredentials credentials);

    IChatClient CreateChatClient(LLMModel model, AIOptions configuration, IAiCredentials credentials);

    // The endpoint pins one client to a resolved model's endpoint; without it a hosted provider uses its registration.
    IChatClient CreateChatClient(string model, AIOptions configuration, IAiCredentials credentials, string? endpoint = null);

    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        AIOptions configuration,
        IAiCredentials credentials);
}