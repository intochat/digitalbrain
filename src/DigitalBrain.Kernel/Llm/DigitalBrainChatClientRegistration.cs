namespace DigitalBrain.Kernel.Llm;

using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using DigitalBrain.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Registers one keyed IChatClient per LLM model the Aspire host declared (see
// DigitalBrainBuilderExtensions.WithModelRegistry), so grains can resolve a specific registered model's
// client via GetRequiredKeyedService instead of only ever getting the single flat unkeyed default.
public static class DigitalBrainChatClientRegistration
{
    public static IServiceCollection AddDigitalBrainChatClients(this IServiceCollection services, IConfiguration config)
    {
        var runtimeOptions = DigitalBrainLlmRuntimeOptions.FromConfiguration(config);
        var entries = DigitalBrainModelRegistrySnapshot.Read(config);

        foreach (var entry in entries)
        {
            if (entry.Kind != DigitalBrainCapabilityKind.LargeLanguageModel || string.IsNullOrWhiteSpace(entry.ServiceKey))
            {
                continue;
            }

            services.AddKeyedSingleton<IChatClient>(entry.ServiceKey, (_, _) =>
                entry.Provider switch
                {
                    var p when string.Equals(p, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase) => BuildAzureOpenAi(runtimeOptions, entry.Id),
                    var p when string.Equals(p, DigitalBrainProviderIds.Anthropic, StringComparison.OrdinalIgnoreCase) => BuildAnthropic(runtimeOptions, entry.Id),
                    var p when string.Equals(p, DigitalBrainProviderIds.Xai, StringComparison.OrdinalIgnoreCase) => BuildXai(runtimeOptions, entry.Id),
                    _ => DigitalBrainChatClients.BuildOllama(runtimeOptions.OllamaEndpoint, entry.Id)
                });
        }

        return services;
    }

    // Mirrors DigitalBrainChat.AddDigitalBrainChat's azureopenai branch exactly (same AzureKeyCredential/
    // DefaultAzureCredential fallback), just keyed per-registration by deployment id instead of the single
    // global model.
    private static IChatClient BuildAzureOpenAi(DigitalBrainLlmRuntimeOptions options, string deploymentId)
    {
        if (string.IsNullOrWhiteSpace(options.AzureOpenAIEndpoint))
        {
            throw new InvalidOperationException(
                $"Registered azureopenai model '{deploymentId}' has no DigitalBrain:Llm:AzureOpenAIEndpoint configured.");
        }

        var azureClient = (string.IsNullOrWhiteSpace(options.AzureOpenAIKey)
                ? new AzureOpenAIClient(new Uri(options.AzureOpenAIEndpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(options.AzureOpenAIEndpoint), new AzureKeyCredential(options.AzureOpenAIKey)))
            .GetChatClient(deploymentId)
            .AsIChatClient();

        return new ChatClientBuilder(azureClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
    }

    // Official anthropics/anthropic-sdk-csharp AsIChatClient() is [Experimental("MEAI001")] — suppressed
    // repo-wide in DigitalBrain.Kernel.csproj's <NoWarn>, matching the existing ORLEANSEXP005 pattern.
    private static IChatClient BuildAnthropic(DigitalBrainLlmRuntimeOptions options, string modelId)
    {
        if (string.IsNullOrWhiteSpace(options.AnthropicApiKey))
        {
            throw new InvalidOperationException(
                $"Registered anthropic model '{modelId}' has no DigitalBrain:Llm:AnthropicApiKey configured.");
        }

        var client = new Anthropic.AnthropicClient { ApiKey = options.AnthropicApiKey };
        return new ChatClientBuilder(client.AsIChatClient(modelId))
            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
            .Build();
    }

    // xAI has no dedicated SDK — Grok's API is OpenAI-API-compatible, so this reuses the official OpenAI
    // .NET SDK pointed at x.ai's base URL via OpenAIClientOptions.Endpoint instead of the default OpenAI one.
    private static IChatClient BuildXai(DigitalBrainLlmRuntimeOptions options, string modelId)
    {
        if (string.IsNullOrWhiteSpace(options.XaiApiKey))
        {
            throw new InvalidOperationException(
                $"Registered xai model '{modelId}' has no DigitalBrain:Llm:XaiApiKey configured.");
        }

        var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.x.ai/v1") };
        var openAiClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(options.XaiApiKey), clientOptions);
        return new ChatClientBuilder(openAiClient.GetChatClient(modelId).AsIChatClient())
            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
            .Build();
    }
}
