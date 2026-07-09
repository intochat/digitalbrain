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
                string.Equals(entry.Provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase)
                    ? BuildAzureOpenAi(runtimeOptions, entry.Id)
                    : DigitalBrainChatClients.BuildOllama(runtimeOptions.OllamaEndpoint, entry.Id));
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
}
