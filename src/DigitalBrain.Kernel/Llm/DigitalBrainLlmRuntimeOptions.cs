using DigitalBrain.Core.Models;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel.Llm;

/// <summary>
/// Resolved language-model runtime configuration consumed by kernel chat factories.
/// </summary>
public sealed record DigitalBrainLlmRuntimeOptions(
    string? Provider,
    string Model,
    string OllamaEndpoint,
    string? AzureOpenAIEndpoint,
    string? AzureOpenAIKey,
    string? AnthropicApiKey,
    string? XaiApiKey,
    string? OpenAIApiKey,
    string? GitHubModelsToken,
    string GitHubModelsEndpoint,
    string OpenAIModel,
    bool EnableSensitiveTelemetry)
{
    public const string DefaultOllamaModel = "llama3.1:8b";
    public const string DefaultOpenAIModel = "gpt-4o-mini";
    public const string DefaultGitHubModelsModel = "openai/gpt-4.1-mini";
    public const string DefaultGitHubModelsEndpoint = "https://models.github.ai/inference";

    /// <summary>
    /// Builds runtime LLM options from the registry-shaped configuration emitted by Aspire,
    /// falling back to the legacy DigitalBrain:Llm keys for older AppHosts and tests.
    /// </summary>
    public static DigitalBrainLlmRuntimeOptions FromConfiguration(IConfiguration config)
    {
        var registryProvider = config["DigitalBrain:ModelRegistry:DefaultLlm:Provider"];
        var registryModel = config["DigitalBrain:ModelRegistry:DefaultLlm:Id"];
        var provider = FirstNonWhiteSpace(registryProvider, config["DigitalBrain:Llm:Provider"]);
        var model = FirstNonWhiteSpace(registryModel, config["DigitalBrain:Llm:Model"])
            ?? DefaultModelForProvider(provider);

        return new DigitalBrainLlmRuntimeOptions(
            provider,
            model,
            config["DigitalBrain:Llm:OllamaEndpoint"] ?? "http://localhost:11434",
            config["DigitalBrain:Llm:AzureOpenAIEndpoint"],
            config["DigitalBrain:Llm:AzureOpenAIKey"],
            config["DigitalBrain:Llm:AnthropicApiKey"],
            config["DigitalBrain:Llm:XaiApiKey"],
            config["DigitalBrain:Llm:OpenAIApiKey"],
            config["DigitalBrain:Llm:GitHubModelsToken"],
            config["DigitalBrain:Llm:GitHubModelsEndpoint"] ?? DefaultGitHubModelsEndpoint,
            FindRegisteredLlmModel(config, DigitalBrainProviderIds.OpenAI)
                ?? config["DigitalBrain:Llm:OpenAIModel"]
                ?? DefaultOpenAIModel,
            bool.TryParse(config["DigitalBrain:Llm:EnableSensitiveTelemetry"], out var sensitiveTelemetry) &&
            sensitiveTelemetry);
    }

    private static string? FindRegisteredLlmModel(IConfiguration config, string provider)
    {
        var entries = DigitalBrainModelRegistrySnapshot.Read(config);
        var match = DigitalBrainModelRegistrySnapshot.FirstOrDefault(
            entries,
            DigitalBrainCapabilityKind.LargeLanguageModel,
            entry => string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match?.Id) ? null : match.Id;
    }

    private static string? FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static string DefaultModelForProvider(string? provider)
    {
        if (string.Equals(provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultOpenAIModel;
        }

        if (string.Equals(provider, DigitalBrainProviderIds.GitHubModels, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultGitHubModelsModel;
        }

        return DefaultOllamaModel;
    }
}
