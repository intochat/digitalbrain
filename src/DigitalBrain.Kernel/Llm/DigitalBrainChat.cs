using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using DigitalBrain.Kernel.Contracts.Models;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DigitalBrain.Kernel.Llm;

internal static class DigitalBrainChat
{

    public static IServiceCollection AddDigitalBrainChat(this IServiceCollection services, IConfiguration config, TokenCredential? azureCredential = null)
    {
        var options = DigitalBrainLlmRuntimeOptions.FromConfiguration(config);

        if (string.Equals(options.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase))
        {
            services.AddChatClient(DigitalBrainChatClients.BuildOllama(options.OllamaEndpoint, options.Model));
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = options.AzureOpenAIEndpoint
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AzureOpenAIEndpoint is required for azureopenai provider.");

            var azureClient = (string.IsNullOrWhiteSpace(options.AzureOpenAIKey)
                                ? new AzureOpenAIClient(new Uri(endpoint), azureCredential ?? new DefaultAzureCredential())
                                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(options.AzureOpenAIKey)))
                            .GetChatClient(options.Model)
                            .AsIChatClient();
            var chatClient = DigitalBrainChatTelemetry.Wrap(azureClient);
            services.AddChatClient(chatClient);
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = options.OpenAIApiKey ?? throw new InvalidOperationException("DigitalBrain:Llm:OpenAIApiKey is required for openai provider.");
            services.AddChatClient(DigitalBrainChatClients.BuildOpenAi(options.Model, apiKey));
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.Anthropic, StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = options.AnthropicApiKey
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AnthropicApiKey is required for anthropic provider.");
            var client = new Anthropic.AnthropicClient { ApiKey = apiKey };
            services.AddChatClient(DigitalBrainChatTelemetry.Wrap(client.AsIChatClient(options.Model)));
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.Xai, StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = options.XaiApiKey ?? throw new InvalidOperationException("DigitalBrain:Llm:XaiApiKey is required for xai provider.");
            services.AddChatClient(DigitalBrainChatClients.BuildOpenAiCompatible("https://api.x.ai/v1", options.Model, apiKey));
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.GitHubModels, StringComparison.OrdinalIgnoreCase))
        {
            var token = options.GitHubModelsToken
                ?? throw new InvalidOperationException("DigitalBrain:Llm:GitHubModelsToken is required for github-models provider.");
            services.AddChatClient(DigitalBrainChatClients.BuildGitHubModels(options.GitHubModelsEndpoint, options.Model, token));
        }
        else if (!string.IsNullOrWhiteSpace(options.Provider))
        {
            throw new InvalidOperationException($"Unsupported LLM provider '{options.Provider}'.");
        }

        var embeddingOptions = DigitalBrainEmbeddingRuntimeOptions.FromConfiguration(config);
        if (string.Equals(embeddingOptions.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(embeddingOptions.Model))
        {
            var embeddingClient = new OllamaApiClient(new Uri(embeddingOptions.OllamaEndpoint), embeddingOptions.Model);
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingClient);
        }
        else
        {

            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
        }
        return services;
    }
}
