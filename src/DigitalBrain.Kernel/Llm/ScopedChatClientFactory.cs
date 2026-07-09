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

            return DigitalBrainChatClients.BuildOpenAi(options.OpenAIModel, apiKey);
        }

        // Default / "ollama": mirror DigitalBrainChat's Ollama wiring.
        return DigitalBrainChatClients.BuildOllama(options.OllamaEndpoint, options.Model);
    }
}

// Shared, provider-id-driven IChatClient construction, used by both the per-request scoped factory above
// and the startup-time keyed registration in DigitalBrainChatClientRegistration.
internal static class DigitalBrainChatClients
{
    public static IChatClient BuildOllama(string endpoint, string model) =>
        new ChatClientBuilder(new OllamaSharp.OllamaApiClient(new Uri(endpoint), model))
            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
            .Build();

    public static IChatClient BuildOpenAi(string model, string apiKey) =>
        new ChatClientBuilder(new OpenAI.Chat.ChatClient(model, apiKey).AsIChatClient())
            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
            .Build();
}
