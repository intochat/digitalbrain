namespace DigitalBrain.Aspire;

/// <summary>
/// Stable provider ids used by the Aspire DSL and kernel configuration.
/// </summary>
public static class DigitalBrainProviderIds
{
    public const string Ollama = "ollama";
    public const string AzureOpenAI = "azureopenai";
    public const string OpenAI = "openai";
    public const string Anthropic = "anthropic";
    public const string GitHubModels = "github-models";
    public const string Xai = "xai";
    public const string Qdrant = "qdrant";
}

/// <summary>
/// Capability type represented by a provider/model registration.
/// </summary>
public enum DigitalBrainCapabilityKind
{
    LargeLanguageModel,
    Embedding,
    VoiceToText,
    VectorDatabase
}

/// <summary>
/// Runtime routing role for language models. The current kernel consumes one selected model;
/// the registry keeps the richer fast/balanced/reasoning shape for the next routing pass.
/// </summary>
public enum DigitalBrainModelRole
{
    Default,
    Fast,
    Balanced,
    Reasoning
}

/// <summary>
/// Provider/model metadata registered by the Aspire DSL.
/// </summary>
public sealed record DigitalBrainModelDescriptor(
    DigitalBrainCapabilityKind Kind,
    string Provider,
    string Id,
    string DisplayName);

/// <summary>
/// A configured provider/model capability and its intended routing role.
/// </summary>
public sealed record DigitalBrainModelRegistration(
    DigitalBrainModelDescriptor Model,
    DigitalBrainModelRole Role);

/// <summary>
/// Mutable registry built by <see cref="DigitalBrainOptions"/> during AppHost configuration.
/// </summary>
public sealed class DigitalBrainModelRegistry
{
    private readonly List<DigitalBrainModelRegistration> registrations = [];

    public DigitalBrainModelRegistry()
    {
    }

    private DigitalBrainModelRegistry(IEnumerable<DigitalBrainModelRegistration> registrations)
    {
        this.registrations.AddRange(registrations);
    }

    /// <summary>
    /// Provider/model registrations in the order the AppHost declared them.
    /// </summary>
    public IReadOnlyList<DigitalBrainModelRegistration> Registrations => registrations;

    /// <summary>
    /// Returns the preferred language model for single-model kernel consumers.
    /// </summary>
    public DigitalBrainModelRegistration? DefaultLlm =>
        registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.LargeLanguageModel &&
            x.Role == DigitalBrainModelRole.Balanced)
        ?? registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.LargeLanguageModel &&
            x.Role == DigitalBrainModelRole.Reasoning)
        ?? registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.LargeLanguageModel &&
            x.Role == DigitalBrainModelRole.Fast)
        ?? registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.LargeLanguageModel);

    internal int Register(DigitalBrainModelDescriptor model, DigitalBrainModelRole role)
    {
        registrations.Add(new DigitalBrainModelRegistration(model, role));
        return registrations.Count - 1;
    }

    internal void SetRole(int index, DigitalBrainModelRole role)
    {
        registrations[index] = registrations[index] with { Role = role };
    }

    internal DigitalBrainModelRegistry Snapshot() => new(registrations);
}

/// <summary>
/// Typed provider/model marker used by the Aspire DSL instead of raw strings.
/// </summary>
public abstract class DigitalBrainModel
{
    public abstract DigitalBrainCapabilityKind Kind { get; }
    public abstract string Provider { get; }
    public abstract string Id { get; }

    public virtual string DisplayName => Id;

    internal DigitalBrainModelDescriptor Describe() => new(Kind, Provider, Id, DisplayName);
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
