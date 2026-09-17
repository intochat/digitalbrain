using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

public sealed class AIOptions
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
    public Dictionary<string, AIModelProfileOptions> ModelProfiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal AIProviderOptions Provider(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => OpenAI,
        AiProvider.Anthropic => Anthropic,
        AiProvider.Google => Google,
        AiProvider.XAI => XAI,
        AiProvider.Ollama => Ollama,
        _ => throw new NotSupportedException($"{provider} has no hosted provider settings."),
    };

    internal static AIOptions Read(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<AIOptions>() ?? new();
        ProjectLegacyKeys(options, configuration);
        return options;
    }

    internal static void Register(IServiceCollection services)
    {
        services.AddOptions<AIOptions>().BindConfiguration(SectionName)
            .Configure<IConfiguration>(ProjectLegacyKeys);
        services.AddOptions<AIWorkspaceOptions>().BindConfiguration(AIWorkspaceOptions.SectionName);
    }

    // Model markers are open-ended child keys beside Endpoint, rather than under Models.
    // Keep that public configuration shape and the standard telemetry fallback at the boundary.
    private static void ProjectLegacyKeys(AIOptions options, IConfiguration configuration)
    {
        foreach (var section in configuration.GetSection($"{SectionName}:Ollama").GetChildren())
        {
            if (section["Model"] is { } model)
            {
                options.Ollama.Models[section.Key] = new AIModelOptions { Model = model };
            }
        }
        options.Telemetry.EnableSensitiveData ??=
            configuration.GetValue<bool?>("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT");
    }
}

public sealed class AIDefaultOptions
{
    public string? Profile { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Reasoning { get; set; }
    public int? MaxOutputTokens { get; set; }
    public LlmCapabilities? Capabilities { get; set; }
    public string? Embedding { get; set; }
    public string? Transcription { get; set; }
    public string? Image { get; set; }
}

public sealed class AIModelProfileOptions
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Endpoint { get; set; }
    public string? Reasoning { get; set; }
    public int? MaxOutputTokens { get; set; }
    public LlmCapabilities? Capabilities { get; set; }
}

public sealed class AITelemetryOptions
{
    public bool? EnableSensitiveData { get; set; }
}

public class AIProviderOptions
{
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
}

public sealed class OllamaOptions : AIProviderOptions
{
    public Dictionary<string, AIModelOptions> Models { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AIModelOptions
{
    public string? Model { get; set; }
}

public sealed class TavilyOptions
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
}
