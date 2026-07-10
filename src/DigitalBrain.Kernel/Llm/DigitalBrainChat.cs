using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using DigitalBrain.Core.Models;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DigitalBrain.Kernel.Llm;

public static class DigitalBrainChat
{
    // azureCredential lets callers pass the process's single shared DefaultAzureCredential (see Program.cs's
    // storageCredential, built once for the managed-identity storage path) instead of this method minting its
    // own; DefaultAzureCredential is resource-agnostic (the same instance authenticates against Storage,
    // Cognitive Services, or anything else backed by Entra ID), and reusing it avoids running its
    // credential-chain probing more than once per process. Null (the default, used by every existing caller
    // and test) falls back to constructing one locally so nothing else has to change.
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
            // No key configured (Task 19, step 3) falls back to DefaultAzureCredential, which resolves the
            // container app's system-assigned managed identity in ACA (granted "Cognitive Services OpenAI
            // User" in deploy/Program.cs) instead of throwing; a configured key keeps working unchanged for
            // local/test usage and until a verified follow-up deploy removes the key wiring entirely.
            var azureClient = (string.IsNullOrWhiteSpace(options.AzureOpenAIKey)
                    ? new AzureOpenAIClient(new Uri(endpoint), azureCredential ?? new DefaultAzureCredential())
                    : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(options.AzureOpenAIKey)))
                .GetChatClient(options.Model)
                .AsIChatClient();
            var chatClient = new ChatClientBuilder(azureClient)
                .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron", configure: static options => options.EnableSensitiveData = false)
                .Build();
            services.AddChatClient(chatClient);
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = options.OpenAIApiKey
                ?? throw new InvalidOperationException("DigitalBrain:Llm:OpenAIApiKey is required for openai provider.");
            services.AddChatClient(DigitalBrainChatClients.BuildOpenAi(options.Model, apiKey));
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.Anthropic, StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = options.AnthropicApiKey
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AnthropicApiKey is required for anthropic provider.");
            var client = new Anthropic.AnthropicClient { ApiKey = apiKey };
            services.AddChatClient(new ChatClientBuilder(client.AsIChatClient(options.Model))
                .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron", configure: static options => options.EnableSensitiveData = false)
                .Build());
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.Xai, StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = options.XaiApiKey
                ?? throw new InvalidOperationException("DigitalBrain:Llm:XaiApiKey is required for xai provider.");
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
        // No provider -> no IChatClient registered; neurons fall back deterministically.

        var embeddingOptions = DigitalBrainEmbeddingRuntimeOptions.FromConfiguration(config);
        if (string.Equals(embeddingOptions.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(embeddingOptions.Model))
        {
            var embeddingClient = new OllamaApiClient(new Uri(embeddingOptions.OllamaEndpoint), embeddingOptions.Model);
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingClient);
        }
        else
        {
            // No embedding provider configured → NoOp fail-soft; HybridScorer (DigitalBrain.Context)
            // detects its zero vectors and falls back to keyword recall.
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
        }
        return services;
    }
}
