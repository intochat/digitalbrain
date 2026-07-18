using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Models;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel.Llm;

internal sealed class ScopedChatClientFactory(IConfiguration config, ILogger<ScopedChatClientFactory> logger) : IScopedChatClientFactory
{
    public IChatClient? Create(string provider, string? apiKey)
    {
        var options = DigitalBrainLlmRuntimeOptions.FromConfiguration(config);
        if (string.Equals(provider, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning("openai provider requested but no API key is configured — falling back to global client.");
                return null;
            }
            return DigitalBrainChatClients.BuildOpenAi(options.OpenAIModel, apiKey);
        }
        if (string.Equals(provider, DigitalBrainProviderIds.GitHubModels, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning("github-models provider requested but no token is configured — falling back to global client.");
                return null;
            }
            return DigitalBrainChatClients.BuildGitHubModels(options.GitHubModelsEndpoint, options.Model, apiKey);
        }
        if (string.IsNullOrWhiteSpace(provider) || string.Equals(provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase))
        {
            return DigitalBrainChatClients.BuildOllama(options.OllamaEndpoint, options.Model);
        }
        logger.LogWarning("Unsupported scoped LLM provider '{Provider}' requested — falling back to global client.", provider);
        return null;
    }
}
internal static class DigitalBrainChatClients
{
    public static IChatClient BuildOllama(string endpoint, string model) =>
        DigitalBrainChatTelemetry.Wrap(new OllamaSharp.OllamaApiClient(new Uri(endpoint), model));
    public static IChatClient BuildOpenAi(string model, string apiKey) =>
        DigitalBrainChatTelemetry.Wrap(new OpenAI.Chat.ChatClient(model, apiKey).AsIChatClient());
    public static IChatClient BuildOpenAiCompatible(string endpoint, string model, string apiKey) =>
        DigitalBrainChatTelemetry.Wrap(
            new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint) })
                .GetChatClient(model)
                .AsIChatClient());
    public static IChatClient BuildGitHubModels(string endpoint, string model, string token) =>
        BuildOpenAiCompatible(endpoint, model, token);
}
internal static class DigitalBrainChatTelemetry
{
    public static IChatClient Wrap(IChatClient client, DigitalBrainChatPolicyOptions? policy = null) =>
        new ChatClientBuilder(client).Use(inner => new BoundedNoRetryChatClient(inner, policy ?? DigitalBrainChatPolicyOptions.Default))
            .UseOpenTelemetry(
                sourceName: "DigitalBrain.Neuron",
                configure: options => options.EnableSensitiveData = false)
            .Build();
}
