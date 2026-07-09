namespace DigitalBrain.Core.Models;

/// <summary>
/// Typed provider/model marker used by the Aspire DSL instead of raw strings.
/// </summary>
public abstract class DigitalBrainModel
{
    public abstract DigitalBrainCapabilityKind Kind { get; }
    public abstract string Provider { get; }
    public abstract string Id { get; }

    public virtual string DisplayName => Id;
    public virtual DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.FullyCapable;

    public DigitalBrainModelDescriptor Describe() => new(Kind, Provider, Id, DisplayName, Capabilities);
}

/// <summary>
/// Typed LLM marker for <see cref="DigitalBrainOptions.WithLLM{TModel}"/>.
/// </summary>
public abstract class LlmModel : DigitalBrainModel
{
    public sealed override DigitalBrainCapabilityKind Kind => DigitalBrainCapabilityKind.LargeLanguageModel;
}

/// <summary>
/// Typed embedding marker for <see cref="DigitalBrainOptions.WithEmbedding{TModel}"/>.
/// </summary>
public abstract class EmbeddingModel : DigitalBrainModel
{
    public sealed override DigitalBrainCapabilityKind Kind => DigitalBrainCapabilityKind.Embedding;
}

/// <summary>
/// Typed voice-to-text marker for <see cref="DigitalBrainOptions.WithVoice2Text{TModel}"/>.
/// </summary>
public abstract class VoiceToTextModel : DigitalBrainModel
{
    public sealed override DigitalBrainCapabilityKind Kind => DigitalBrainCapabilityKind.VoiceToText;
}

/// <summary>
/// Local Ollama fallback model used by the default development AppHost.
/// </summary>
public sealed class Qwen25Coder1_5B : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Ollama;
    public override string Id => "qwen2.5-coder:1.5b";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ChatOnly;
}

/// <summary>
/// Azure OpenAI chat deployment default. Override <see cref="DigitalBrainOptions.LlmModel"/>
/// when the Azure deployment name is not the model id.
/// </summary>
public sealed class Gpt4oMini : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.AzureOpenAI;
    public override string Id => "gpt-4o-mini";
}

/// <summary>
/// Local OpenAI-compatible Whisper transcription endpoint.
/// </summary>
public sealed class Whisper1Local : VoiceToTextModel
{
    public override string Provider => DigitalBrainProviderIds.OpenAICompatible;
    public override string Id => "whisper-1";
    public override string DisplayName => "Local Whisper";
}

// Local Ollama embedding model — 768-dim, drop-in replacement for NoOpEmbeddingGenerator's 384-dim zero
// vectors. HybridScorer (DigitalBrain.Context/HybridScorer.cs) already detects zero vectors and falls back
// to keyword recall, so wiring this activates vector RAG with no change to HybridScorer itself.
public sealed class NomicEmbedText : EmbeddingModel
{
    public override string Provider => DigitalBrainProviderIds.Ollama;
    public override string Id => "nomic-embed-text";
}
