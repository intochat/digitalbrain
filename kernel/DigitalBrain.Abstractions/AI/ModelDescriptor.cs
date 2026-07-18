namespace DigitalBrain;

public enum ModelProvider
{
    OpenAI,
    Anthropic
}

public enum ModelCapability
{
    Chat,
    Embedding
}

public abstract record ModelDescriptor(ModelProvider Provider, string ModelId, ModelCapability Capability)
{
    public Uri? Endpoint { get; init; }
}

public abstract record ChatModelDescriptor(ModelProvider Provider, string ModelId)
    : ModelDescriptor(Provider, ModelId, ModelCapability.Chat);

public abstract record EmbeddingModelDescriptor(ModelProvider Provider, string ModelId)
    : ModelDescriptor(Provider, ModelId, ModelCapability.Embedding);

public sealed record GptFast() : ChatModelDescriptor(ModelProvider.OpenAI, "gpt-5-mini");

public sealed record ClaudeBalanced() : ChatModelDescriptor(ModelProvider.Anthropic, "claude-sonnet-4-5");

public sealed record GptReasoning() : ChatModelDescriptor(ModelProvider.OpenAI, "gpt-5");

public sealed record TextEmbedding() : EmbeddingModelDescriptor(ModelProvider.OpenAI, "text-embedding-3-small");
