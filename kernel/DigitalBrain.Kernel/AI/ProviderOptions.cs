namespace DigitalBrain.Kernel;

internal sealed class OpenAIProviderOptions
{
    public const string SectionName = "DigitalBrain:AI:OpenAI";

    public string? ApiKey { get; set; }
    public Uri? Endpoint { get; set; }
    public string? FastModelId { get; set; }
    public string? ReasoningModelId { get; set; }
    public string? EmbeddingModelId { get; set; }
}

internal sealed class AnthropicProviderOptions
{
    public const string SectionName = "DigitalBrain:AI:Anthropic";

    public string? ApiKey { get; set; }
    public Uri? Endpoint { get; set; }
    public string? BalancedModelId { get; set; }
}

internal sealed record DigitalBrainAIHttpClients(
    HttpClient? OpenAI = null,
    HttpClient? Anthropic = null);
