namespace DigitalBrain.Kernel.Contracts.Models;

public static class DigitalBrainProviderIds
{
    public const string Ollama = "ollama";
    public const string AzureOpenAI = "azureopenai";
    public const string OpenAI = "openai";
    public const string Anthropic = "anthropic";
    public const string GitHubModels = "github-models";
    public const string Xai = "xai";
}

public enum DigitalBrainCapabilityKind
{
    LargeLanguageModel,
    Embedding
}

public enum DigitalBrainModelRole
{
    Default,
    Fast,
    Balanced,
    Reasoning
}

public sealed record DigitalBrainModelCapabilities(bool SupportsTools, bool SupportsVision, bool SupportsStreaming, bool SupportsStructuredOutput)
{
    public static readonly DigitalBrainModelCapabilities FullyCapable = new(true, true, true, true);
    public static readonly DigitalBrainModelCapabilities ChatOnly = new(false, false, true, false);
    public static readonly DigitalBrainModelCapabilities ToolCapable = new(true, false, true, true);
}

public sealed record DigitalBrainModelDescriptor(DigitalBrainCapabilityKind Kind, string Provider, string Id, string DisplayName, DigitalBrainModelCapabilities Capabilities)
{

    public string ServiceKey => Normalize($"{Provider}-{Id}");

    private static string Normalize(string value) =>
        value.Replace(':', '-').Replace('.', '-').ToLowerInvariant();
}

public sealed record DigitalBrainModelRegistration(DigitalBrainModelDescriptor Model, DigitalBrainModelRole Role);

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

    public IReadOnlyList<DigitalBrainModelRegistration> Registrations => registrations;

    public DigitalBrainModelRegistration? DefaultLlm =>
            registrations.LastOrDefault(static x =>
                x.Model.Kind == DigitalBrainCapabilityKind.LargeLanguageModel && x.Role == DigitalBrainModelRole.Balanced)
            ?? registrations.LastOrDefault(static x =>
                x.Model.Kind == DigitalBrainCapabilityKind.LargeLanguageModel && x.Role == DigitalBrainModelRole.Reasoning)
            ?? registrations.LastOrDefault(static x =>
                x.Model.Kind == DigitalBrainCapabilityKind.LargeLanguageModel && x.Role == DigitalBrainModelRole.Fast)
            ?? registrations.LastOrDefault(static x =>
                x.Model.Kind == DigitalBrainCapabilityKind.LargeLanguageModel);

    public DigitalBrainModelRegistration? DefaultEmbedding =>
        registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.Embedding && x.Role == DigitalBrainModelRole.Default)
        ?? registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.Embedding);

    public int Register(DigitalBrainModelDescriptor model, DigitalBrainModelRole role)
    {
        registrations.Add(new DigitalBrainModelRegistration(model, role));
        return registrations.Count - 1;
    }

    public void SetRole(int index, DigitalBrainModelRole role)
    {
        registrations[index] = registrations[index] with { Role = role };
    }

    public DigitalBrainModelRegistry Snapshot() => new(registrations);
}
