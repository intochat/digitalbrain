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
