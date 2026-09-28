using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal interface ILlmProviderFactory
{
    AiProvider Provider { get; }

    bool IsConfigured(AIOptions configuration);

    IChatClient CreateChatClient(LLMModel model, AIOptions configuration);

    IChatClient CreateChatClient(string model, AIOptions configuration);

    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        AIOptions configuration);
}