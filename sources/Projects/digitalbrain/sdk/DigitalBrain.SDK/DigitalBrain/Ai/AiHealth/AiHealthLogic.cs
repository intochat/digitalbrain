using DigitalBrain.SDK.DigitalBrain.Ai.Llm.Providers;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.SDK.DigitalBrain.Ai.AiHealth;

public sealed class AiHealthLogic(IConfiguration configuration) : IAiHealthLogic
{
    static readonly IReadOnlyList<ILlmProviderFactory> Factories =
    [
        new OpenAiProviderFactory(),
        new AnthropicProviderFactory(),
        new OllamaProviderFactory(),
    ];

    public AiHealthStatus Inspect()
    {
        var useMock = string.Equals(
            configuration["DigitalBrain:Ai:UseMockClient"], "true", StringComparison.OrdinalIgnoreCase);
        if (useMock)
            return new AiHealthStatus(Live: false, Reason: "Mock LLM client is active.", Model: "");

        var configuredModels = FindConfiguredModels();
        if (configuredModels.Count == 0)
            return new AiHealthStatus(Live: false, Reason: "No provider configured.", Model: "");

        return new AiHealthStatus(Live: true, Reason: "", Model: configuredModels[^1].Id);
    }

    List<LlmModel> FindConfiguredModels()
    {
        var factoryMap = Factories.ToDictionary(f => f.ProviderName, StringComparer.Ordinal);
        return [.. LlmModel.All
            .Where(m => factoryMap.TryGetValue(m.Provider, out var factory) && factory.IsConfigured(configuration))];
    }
}
