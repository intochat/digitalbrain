using Microsoft.Extensions.AI;
using DigitalBrain.Core;
using DigitalBrain.Core.Models;
using DigitalBrain.Kernel;

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

            var openAiClient = new OpenAI.Chat.ChatClient(options.OpenAIModel, apiKey).AsIChatClient();
            return new ChatClientBuilder(openAiClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
        }

        // Default / "ollama": mirror DigitalBrainChat's Ollama wiring.
        var ollamaClient = new OllamaSharp.OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model);
        return new ChatClientBuilder(ollamaClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
    }
}
