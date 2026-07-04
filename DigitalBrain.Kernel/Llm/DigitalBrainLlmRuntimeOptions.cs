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
    string OpenAIModel)
{
    public const string DefaultOllamaModel = "qwen2.5-coder:1.5b";
    public const string DefaultOpenAIModel = "gpt-4o-mini";

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
            ?? (string.Equals(provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase)
                ? DefaultOpenAIModel
                : DefaultOllamaModel);

        return new DigitalBrainLlmRuntimeOptions(
            provider,
            model,
            config["DigitalBrain:Llm:OllamaEndpoint"] ?? "http://localhost:11434",
            config["DigitalBrain:Llm:AzureOpenAIEndpoint"],
            config["DigitalBrain:Llm:AzureOpenAIKey"],
            FindRegisteredLlmModel(config, DigitalBrainProviderIds.OpenAI)
                ?? config["DigitalBrain:Llm:OpenAIModel"]
                ?? DefaultOpenAIModel);
    }

    private static string? FindRegisteredLlmModel(IConfiguration config, string provider)
    {
        foreach (var child in config.GetSection("DigitalBrain:ModelRegistry:Registrations").GetChildren())
        {
            var kind = child["Kind"];
            var registrationProvider = child["Provider"];
            if (!string.Equals(kind, DigitalBrainCapabilityKind.LargeLanguageModel.ToString(), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(registrationProvider, provider, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var model = child["Id"];
            if (!string.IsNullOrWhiteSpace(model))
            {
                return model;
            }
        }

        return null;
    }

    private static string? FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}
