using DigitalBrain.Core;
using DigitalBrain.Core.Models;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Llm;

// Builds per-scope chat clients. Ollama mirrors DigitalBrainChat (endpoint/model from kernel config);
// OpenAI is constructed from the caller-supplied key. The key is never logged.
public sealed class ScopedChatClientFactory(IConfiguration config, ILogger<ScopedChatClientFactory> logger) : IScopedChatClientFactory
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

            return DigitalBrainChatClients.BuildOpenAi(options.OpenAIModel, apiKey, options.EnableSensitiveTelemetry);
        }

        if (string.Equals(provider, DigitalBrainProviderIds.GitHubModels, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning("github-models provider requested but no token is configured — falling back to global client.");
                return null;
            }

            return DigitalBrainChatClients.BuildGitHubModels(
                options.GitHubModelsEndpoint,
                options.Model,
                apiKey,
                options.EnableSensitiveTelemetry);
        }

        if (string.IsNullOrWhiteSpace(provider) ||
            string.Equals(provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase))
        {
            return DigitalBrainChatClients.BuildOllama(
                options.OllamaEndpoint,
                options.Model,
                options.EnableSensitiveTelemetry);
        }

        logger.LogWarning("Unsupported scoped LLM provider '{Provider}' requested — falling back to global client.", provider);
        return null;
    }
}

// Shared, provider-id-driven IChatClient construction, used by both the per-request scoped factory above
// and the startup-time keyed registration in DigitalBrainChatClientRegistration.
internal static class DigitalBrainChatClients
{
    public static IChatClient BuildOllama(string endpoint, string model, bool enableSensitiveTelemetry = false) =>
        DigitalBrainChatTelemetry.Wrap(
            new OllamaSharp.OllamaApiClient(new Uri(endpoint), model),
            enableSensitiveTelemetry);

    public static IChatClient BuildOpenAi(string model, string apiKey, bool enableSensitiveTelemetry = false) =>
        DigitalBrainChatTelemetry.Wrap(
            new OpenAI.Chat.ChatClient(model, apiKey).AsIChatClient(),
            enableSensitiveTelemetry);

    public static IChatClient BuildOpenAiCompatible(
        string endpoint,
        string model,
        string apiKey,
        bool enableSensitiveTelemetry = false) =>
        DigitalBrainChatTelemetry.Wrap(
            new OpenAI.OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(apiKey),
                    new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint) })
                .GetChatClient(model)
                .AsIChatClient(),
            enableSensitiveTelemetry);

    public static IChatClient BuildGitHubModels(
        string endpoint,
        string model,
        string token,
        bool enableSensitiveTelemetry = false) =>
        BuildOpenAiCompatible(endpoint, model, token, enableSensitiveTelemetry);
}

public static class DigitalBrainChatTelemetry
{
    public static IChatClient Wrap(IChatClient client, bool enableSensitiveTelemetry) =>
        new ChatClientBuilder(client)
            .UseOpenTelemetry(
                sourceName: "DigitalBrain.Neuron",
                configure: options => options.EnableSensitiveData = enableSensitiveTelemetry)
            .Build();
}
