using DigitalBrain.AI.Web;
using DigitalBrain.AI.Media;
using DigitalBrain.AI.WebSearch;
using DigitalBrain.AI.Agents;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.AI;

[ModuleConfiguration(typeof(AIConfigurationContract))]
[ModuleHosting("DigitalBrain.AI.Aspire.Hosting.AIModuleHosting, DigitalBrain.Modules.AI.Aspire.Hosting")]
public sealed class AIModule : IModule
{
    public const string McpServersKey = "DigitalBrain:AI:McpServers";

    public static ModuleDefinition Define(AIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var section = AIOptions.SectionName;
        var configuration = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Put(configuration, $"{section}:Default:Profile", options.Default.Profile);
        Put(configuration, $"{section}:Default:Provider", options.Default.Provider);
        Put(configuration, $"{section}:Default:Model", options.Default.Model);
        Put(configuration, $"{section}:Default:Reasoning", options.Default.Reasoning);
        Put(configuration, $"{section}:Default:MaxOutputTokens", options.Default.MaxOutputTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Put(configuration, $"{section}:Default:Capabilities", options.Default.Capabilities?.ToString());
        Put(configuration, $"{section}:Default:Embedding", options.Default.Embedding);
        Put(configuration, $"{section}:Default:Transcription", options.Default.Transcription);
        Put(configuration, $"{section}:Default:Image", options.Default.Image);
        Put(configuration, $"{section}:Telemetry:EnableSensitiveData",
            options.Telemetry.EnableSensitiveData is { } sensitive ? sensitive.ToString() : null);
        Put(configuration, $"{section}:Tavily:Enabled", options.Tavily.Enabled.ToString());
        Put(configuration, $"{section}:Ollama:Endpoint", options.Ollama.Endpoint);
        foreach (var provider in new[] { AiProvider.OpenAI, AiProvider.Anthropic, AiProvider.Google, AiProvider.XAI })
        { Put(configuration, $"{section}:{provider}:Endpoint", options.Provider(provider).Endpoint); }
        for (var i = 0; i < options.Hosting.Llms.Count; i++)
        {
            if (LLMModel.FindByMarkerName(options.Hosting.Llms[i]) is null)
            { throw new ArgumentException("Unknown LLM marker in the module declaration.", nameof(options)); }
            Put(configuration, $"{section}:Hosting:Llms:{i}", options.Hosting.Llms[i]);
        }
        for (var i = 0; i < options.Hosting.Embeddings.Count; i++)
        {
            if (EmbeddingModel.FindByMarkerName(options.Hosting.Embeddings[i]) is null)
            { throw new ArgumentException("Unknown embedding marker in the module declaration.", nameof(options)); }
            Put(configuration, $"{section}:Hosting:Embeddings:{i}", options.Hosting.Embeddings[i]);
        }
        foreach (var (name, model) in options.Ollama.Models)
        { Put(configuration, $"{section}:Ollama:Models:{name}:Model", model.Model); }
        foreach (var (name, profile) in options.ModelProfiles)
        {
            var prefix = $"{section}:ModelProfiles:{name}";
            Put(configuration, $"{prefix}:Provider", profile.Provider);
            Put(configuration, $"{prefix}:Model", profile.Model);
            Put(configuration, $"{prefix}:Endpoint", profile.Endpoint);
            Put(configuration, $"{prefix}:Reasoning", profile.Reasoning);
            Put(configuration, $"{prefix}:MaxOutputTokens", profile.MaxOutputTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Put(configuration, $"{prefix}:Capabilities", profile.Capabilities?.ToString());
            Put(configuration, $"{prefix}:ContextWindowTokens", profile.ContextWindowTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Put(configuration, $"{prefix}:MaximumOutputTokens", profile.MaximumOutputTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Put(configuration, $"{prefix}:SupportsTemperature", profile.SupportsTemperature?.ToString());
            Put(configuration, $"{prefix}:SupportsTopP", profile.SupportsTopP?.ToString());
            for (var i = 0; i < profile.AllowedReasoning?.Count; i++)
            { Put(configuration, $"{prefix}:AllowedReasoning:{i}", profile.AllowedReasoning[i]); }
        }
        return new(typeof(AIModule), configuration);
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AIOptions.Register(builder.Services);
        var options = AIOptions.Read(builder.Configuration);
        var workspace = builder.Configuration.GetSection(AIWorkspaceOptions.SectionName).Get<AIWorkspaceOptions>() ?? new();

        AIClients.Add(builder.Services);
        builder.Services.TryAddSingleton<Agents.IAgentTurnRunner, Agents.AgentTurnRunner>();
        builder.Services.TryAddSingleton<ModelProfiles>();
        builder.Services.TryAddSingleton<InferenceService>();
        builder.Services.AddMediaNeurons();
        AIClients.AddImageGeneration(builder.Services, options);
        VoiceToTextHosting.Add(builder.Services, options);
        WebSearchHosting.Add(builder.Services, options);
        RegisterMcpBridge(builder.Services, builder.Configuration[McpServersKey]);

        builder.Services.TryAddSingleton<NativeTools>();
        builder.Services.TryAddSingleton<PlaywrightWebAgent>();
        builder.Services.AddNativeTool("browse_web", services =>
            Microsoft.Extensions.AI.AIFunctionFactory.Create(services.GetRequiredService<PlaywrightWebAgent>().ResearchAsync, "browse_web"));
        builder.Services.AddNativeTool("lookup_company", services =>
            Microsoft.Extensions.AI.AIFunctionFactory.Create(services.GetRequiredService<PlaywrightWebAgent>().LookupCompanyAsync, "lookup_company"));
        if (options.Tavily.Enabled)
        {
            builder.Services.AddNativeTool("websearch", services => WebSearchFunction.Create(services.GetRequiredService<IWebSearch>()));
        }

        if (!string.IsNullOrWhiteSpace(workspace.RepositoryPath))
        {
            builder.Services.AddNativeTool("repositorydiff", services => new RepositoryDiffFunction(services.GetRequiredService<IOptions<AIWorkspaceOptions>>()).Function);
        }
    }

    private static void Put(Dictionary<string, string?> configuration, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[key] = value;
        }
    }

    internal static void RegisterMcpBridge(IServiceCollection services, string? allowlist)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(allowlist))
        {
            return;
        }

        foreach (var entry in allowlist.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                throw new InvalidOperationException($"MCP allowlist entry '{entry}' is not of the form id=https://endpoint.");
            }

            var serverId = entry[..separator].Trim();
            var endpoint = entry[(separator + 1)..].Trim();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException($"MCP allowlist entry '{serverId}' requires an absolute HTTP(S) endpoint.");
            }

            services.AddMcpAgentTools(serverId, uri);
        }
    }
}
