namespace DigitalBrain.Kernel.Llm;

using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using DigitalBrain.Kernel.Contracts.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
internal static class DigitalBrainChatClientRegistration
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
                    var p when string.Equals(p, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase) => DigitalBrainChatClients.BuildOllama(runtimeOptions.OllamaEndpoint, entry.Id),
                    var p when string.Equals(p, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase) => BuildAzureOpenAi(runtimeOptions, entry.Id),
                    var p when string.Equals(p, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase) => BuildOpenAi(runtimeOptions, entry.Id),
                    var p when string.Equals(p, DigitalBrainProviderIds.Anthropic, StringComparison.OrdinalIgnoreCase) => BuildAnthropic(runtimeOptions, entry.Id),
                    var p when string.Equals(p, DigitalBrainProviderIds.Xai, StringComparison.OrdinalIgnoreCase) => BuildXai(runtimeOptions, entry.Id),
                    var p when string.Equals(p, DigitalBrainProviderIds.GitHubModels, StringComparison.OrdinalIgnoreCase) => BuildGitHubModels(runtimeOptions, entry.Id),
                    _ => throw new InvalidOperationException($"Unsupported LLM provider '{entry.Provider}' for registered model '{entry.Id}'.")
                });
        }
        return services;
    }
    private static IChatClient BuildAzureOpenAi(DigitalBrainLlmRuntimeOptions options, string deploymentId)
    {
        if (string.IsNullOrWhiteSpace(options.AzureOpenAIEndpoint))
        {
            throw new InvalidOperationException($"Registered azureopenai model '{deploymentId}' has no DigitalBrain:Llm:AzureOpenAIEndpoint configured.");
        }
        var azureClient = (string.IsNullOrWhiteSpace(options.AzureOpenAIKey)
                ? new AzureOpenAIClient(new Uri(options.AzureOpenAIEndpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(options.AzureOpenAIEndpoint), new AzureKeyCredential(options.AzureOpenAIKey)))
            .GetChatClient(deploymentId)
            .AsIChatClient();
        return DigitalBrainChatTelemetry.Wrap(azureClient);
    }
    private static IChatClient BuildOpenAi(DigitalBrainLlmRuntimeOptions options, string modelId)
    {
        if (string.IsNullOrWhiteSpace(options.OpenAIApiKey))
        {
            throw new InvalidOperationException($"Registered openai model '{modelId}' has no DigitalBrain:Llm:OpenAIApiKey configured.");
        }
        return DigitalBrainChatClients.BuildOpenAi(modelId, options.OpenAIApiKey);
    }
    private static IChatClient BuildAnthropic(DigitalBrainLlmRuntimeOptions options, string modelId)
    {
        if (string.IsNullOrWhiteSpace(options.AnthropicApiKey))
        {
            throw new InvalidOperationException($"Registered anthropic model '{modelId}' has no DigitalBrain:Llm:AnthropicApiKey configured.");
        }
        var client = new Anthropic.AnthropicClient { ApiKey = options.AnthropicApiKey };
        return DigitalBrainChatTelemetry.Wrap(client.AsIChatClient(modelId));
    }
    private static IChatClient BuildXai(DigitalBrainLlmRuntimeOptions options, string modelId)
    {
        if (string.IsNullOrWhiteSpace(options.XaiApiKey))
        {
            throw new InvalidOperationException($"Registered xai model '{modelId}' has no DigitalBrain:Llm:XaiApiKey configured.");
        }
        return DigitalBrainChatClients.BuildOpenAiCompatible("https://api.x.ai/v1", modelId, options.XaiApiKey);
    }
    private static IChatClient BuildGitHubModels(DigitalBrainLlmRuntimeOptions options, string modelId)
    {
        if (string.IsNullOrWhiteSpace(options.GitHubModelsToken))
        {
            throw new InvalidOperationException($"Registered github-models model '{modelId}' has no DigitalBrain:Llm:GitHubModelsToken configured.");
        }
        return DigitalBrainChatClients.BuildGitHubModels(options.GitHubModelsEndpoint, modelId, options.GitHubModelsToken);
    }
}
