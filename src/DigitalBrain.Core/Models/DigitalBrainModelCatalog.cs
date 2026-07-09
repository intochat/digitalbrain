namespace DigitalBrain.Core.Models;

/// <summary>
/// Stable provider ids used by AppHost configuration and kernel runtime routing.
/// </summary>
public static class DigitalBrainProviderIds
{
    public const string Ollama = "ollama";
    public const string AzureOpenAI = "azureopenai";
    public const string OpenAI = "openai";
    public const string OpenAICompatible = "openai-compatible";
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
/// Runtime routing role for language models.
/// </summary>
public enum DigitalBrainModelRole
{
    Default,
    Fast,
    Balanced,
    Reasoning
}

/// <summary>
/// Capability flags for a provider/model registration, orthogonal to <see cref="DigitalBrainModelRole"/>
/// (a quality/cost tier). A Fast-tier model and a Reasoning-tier model can each independently support or
/// lack tool-calling — role says "how good," capabilities say "what it can actually do."
/// </summary>
public sealed record DigitalBrainModelCapabilities(
    bool SupportsTools,
    bool SupportsVision,
    bool SupportsStreaming,
    bool SupportsStructuredOutput)
{
    public static readonly DigitalBrainModelCapabilities FullyCapable = new(true, true, true, true);
    public static readonly DigitalBrainModelCapabilities ChatOnly = new(false, false, true, false);
    public static readonly DigitalBrainModelCapabilities ToolCapable = new(true, false, true, true);
}

/// <summary>
/// Provider/model metadata shared between Aspire configuration and kernel runtime.
/// </summary>
public sealed record DigitalBrainModelDescriptor(
    DigitalBrainCapabilityKind Kind,
    string Provider,
    string Id,
    string DisplayName,
    DigitalBrainModelCapabilities Capabilities)
{
    /// <summary>
    /// Stable identifier for this provider/model pair, safe to use as a .NET keyed-service key
    /// (colons and dots, common in Ollama tags like "llama3.1:8b", are normalized to hyphens).
    /// </summary>
    public string ServiceKey => Normalize($"{Provider}-{Id}");

    private static string Normalize(string value) =>
        value.Replace(':', '-').Replace('.', '-').ToLowerInvariant();
}

/// <summary>
/// A configured provider/model capability and its intended routing role.
/// </summary>
public sealed record DigitalBrainModelRegistration(
    DigitalBrainModelDescriptor Model,
    DigitalBrainModelRole Role);

/// <summary>
/// Mutable provider/model registry built by hosting configuration before it is exported to the kernel.
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
    /// Provider/model registrations in declaration order.
    /// </summary>
    public IReadOnlyList<DigitalBrainModelRegistration> Registrations => registrations;

    /// <summary>
    /// Preferred language model for single-model runtime consumers.
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

    /// <summary>
    /// Preferred voice-to-text model for speech transcription runtime consumers.
    /// </summary>
    public DigitalBrainModelRegistration? DefaultVoiceToText =>
        registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.VoiceToText &&
            x.Role == DigitalBrainModelRole.Default)
        ?? registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.VoiceToText);

    public DigitalBrainModelRegistration? DefaultEmbedding =>
        registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.Embedding &&
            x.Role == DigitalBrainModelRole.Default)
        ?? registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.Embedding);

    /// <summary>
    /// Adds a provider/model registration and returns its index for later role updates.
    /// </summary>
    public int Register(DigitalBrainModelDescriptor model, DigitalBrainModelRole role)
    {
        registrations.Add(new DigitalBrainModelRegistration(model, role));
        return registrations.Count - 1;
    }

    /// <summary>
    /// Updates the routing role for an existing registration.
    /// </summary>
    public void SetRole(int index, DigitalBrainModelRole role)
    {
        registrations[index] = registrations[index] with { Role = role };
    }

    /// <summary>
    /// Returns a copy for contexts that should not observe future option mutations.
    /// </summary>
    public DigitalBrainModelRegistry Snapshot() => new(registrations);
}
