using System.Text.Json.Serialization;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.AI;

public sealed class AIOptions : IModuleOptions
{
    public const string SectionName = "DigitalBrain:AI";
    public AIDefaultOptions Default { get; set; } = new();
    public AITelemetryOptions Telemetry { get; set; } = new();
    public AIProviderOptions OpenAI { get; set; } = new();
    public AIProviderOptions Anthropic { get; set; } = new();
    public AIProviderOptions Google { get; set; } = new();
    public AIProviderOptions XAI { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
    public TavilyOptions Tavily { get; set; } = new();
    public AIHostingOptions Hosting { get; set; } = new();
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, AIModelProfileOptions> ModelProfiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AIProviderOptions Provider(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => OpenAI,
        AiProvider.Anthropic => Anthropic,
        AiProvider.Google => Google,
        AiProvider.XAI => XAI,
        AiProvider.Ollama => Ollama,
        _ => throw new NotSupportedException($"{provider} has no hosted provider settings."),
    };

    public void Validate()
    {
        if (Hosting.Llms.Any(name => LLMModel.FindByMarkerName(name) is null))
        { throw new ArgumentException("Unknown LLM marker in the module declaration."); }
        if (Hosting.Embeddings.Any(name => EmbeddingModel.FindByMarkerName(name) is null))
        { throw new ArgumentException("Unknown embedding marker in the module declaration."); }
    }

    // The declared topology and defaults arrive as the module's options.




    public void CopyFrom(AIOptions source)
    {
        Default = source.Default;
        Telemetry = source.Telemetry;
        OpenAI = source.OpenAI;
        Anthropic = source.Anthropic;
        Google = source.Google;
        XAI = source.XAI;
        Ollama = source.Ollama;
        Tavily = source.Tavily;
        Hosting = source.Hosting;
        ModelProfiles.Clear();
        foreach (var (name, profile) in source.ModelProfiles) { ModelProfiles[name] = profile; }
    }

    // Model markers are open-ended child keys beside Endpoint, rather than under Models.
    // Keep that public configuration shape.

}
