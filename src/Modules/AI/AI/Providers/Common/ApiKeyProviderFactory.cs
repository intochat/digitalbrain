using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal abstract class ApiKeyProviderFactory : ILlmProviderFactory
{
    public abstract AiProvider Provider { get; }

    public bool IsConfigured(AIOptions configuration)
        => !string.IsNullOrEmpty(configuration.Provider(Provider).ApiKey);

    public virtual IChatClient CreateChatClient(LLMModel model, AIOptions configuration)
        => CreateChatClient(model.Id, configuration);

    public abstract IChatClient CreateChatClient(string model, AIOptions configuration);

    public abstract IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        AIOptions configuration);

    protected string ApiKeyConfigurationKey => $"{AIClients.ConfigurationRoot}:{Provider}:ApiKey";

    protected string RequireApiKey(AIOptions configuration, Type marker, string hostingMethod)
        => RequireApiKey(configuration, marker.Name, hostingMethod);

    protected string RequireApiKey(AIOptions configuration, string model, string hostingMethod)
        => configuration.Provider(Provider).ApiKey is { Length: > 0 } apiKey
            ? apiKey
            : throw new InvalidOperationException(
                $"{model} requires {ApiKeyConfigurationKey}. Configure the provider through "
                + $"AIModule.{hostingMethod} in AppHost and supply the "
                + $"{Provider.ToString().ToLowerInvariant()}-api-key secret parameter.");
}