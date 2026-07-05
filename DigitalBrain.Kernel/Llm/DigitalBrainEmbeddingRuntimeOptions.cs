using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel.Llm;

/// <summary>
/// Resolved embedding-model runtime configuration consumed by the fail-soft embedding generator registration.
/// </summary>
public sealed record DigitalBrainEmbeddingRuntimeOptions(
    string? Provider,
    string? Model,
    string OllamaEndpoint)
{
    public const string DefaultOllamaModel = "nomic-embed-text";

    public static DigitalBrainEmbeddingRuntimeOptions FromConfiguration(IConfiguration config)
    {
        var provider = config["DigitalBrain:ModelRegistry:DefaultEmbedding:Provider"];
        var model = config["DigitalBrain:ModelRegistry:DefaultEmbedding:Id"];

        return new DigitalBrainEmbeddingRuntimeOptions(
            provider,
            model,
            config["DigitalBrain:Embedding:OllamaEndpoint"] ?? "http://localhost:11434");
    }
}
