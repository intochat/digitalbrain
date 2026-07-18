using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Kernel;

internal sealed class DigitalBrainAIHealthCheck(
    IOptions<OpenAIProviderOptions> openAI,
    IOptions<AnthropicProviderOptions> anthropic)
    : IHealthCheck
{
    public const string Name = "digitalbrain-ai";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var openAIOptions = openAI.Value;
        var anthropicOptions = anthropic.Value;
        IReadOnlyDictionary<string, object> data = new Dictionary<string, object>
        {
            ["fastModel"] = openAIOptions.FastModelId!,
            ["balancedModel"] = anthropicOptions.BalancedModelId!,
            ["reasoningModel"] = openAIOptions.ReasoningModelId!,
            ["embeddingModel"] = openAIOptions.EmbeddingModelId!
        };
        return Task.FromResult(HealthCheckResult.Healthy(data: data));
    }
}
