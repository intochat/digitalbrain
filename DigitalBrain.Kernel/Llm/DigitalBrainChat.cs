using Azure;
using Azure.AI.OpenAI;
using DigitalBrain.Core.Models;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DigitalBrain.Kernel.Llm;

public static class DigitalBrainChat
{
    public static IServiceCollection AddDigitalBrainChat(this IServiceCollection services, IConfiguration config)
    {
        var options = DigitalBrainLlmRuntimeOptions.FromConfiguration(config);

        if (string.Equals(options.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase))
        {
            var ollamaClient = new OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model);
            var chatClient = new ChatClientBuilder(ollamaClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
            services.AddChatClient(chatClient);
        }
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = options.AzureOpenAIEndpoint
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AzureOpenAIEndpoint is required for azureopenai provider.");
            var key = options.AzureOpenAIKey
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AzureOpenAIKey is required for azureopenai provider.");
            var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
                .GetChatClient(options.Model)
                .AsIChatClient();
            var chatClient = new ChatClientBuilder(azureClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
            services.AddChatClient(chatClient);
        }
        // No provider → no IChatClient registered; neurons fall back deterministically.

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
