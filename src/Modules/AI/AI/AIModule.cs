using DigitalBrain.AI.Web;
using DigitalBrain.AI.WebSearch;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.AI;

public sealed class AIModule : IModule
{
    public static ModuleDefinition Define(AIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var section = AIOptions.SectionName;
        var configuration = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Put(configuration, $"{section}:Default:Profile", options.Default.Profile);
        Put(configuration, $"{section}:Default:Provider", options.Default.Provider);
        Put(configuration, $"{section}:Default:Model", options.Default.Model);
        Put(configuration, $"{section}:Default:Reasoning", options.Default.Reasoning);
        Put(configuration, $"{section}:Default:Embedding", options.Default.Embedding);
        Put(configuration, $"{section}:Default:Transcription", options.Default.Transcription);
        Put(configuration, $"{section}:Default:Image", options.Default.Image);
        Put(configuration, $"{section}:Telemetry:EnableSensitiveData",
            options.Telemetry.EnableSensitiveData is { } sensitive ? sensitive.ToString() : null);
        Put(configuration, $"{section}:Tavily:Enabled", options.Tavily.Enabled.ToString());
        Put(configuration, $"{section}:Ollama:Endpoint", options.Ollama.Endpoint);
        return new(typeof(AIModule), configuration);
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AIOptions.Register(builder.Services);
        var options = AIOptions.Read(builder.Configuration);
        var workspace = builder.Configuration.GetSection(AIWorkspaceOptions.SectionName).Get<AIWorkspaceOptions>() ?? new();

        AIClients.Add(builder.Services);
        builder.Services.TryAddSingleton<ModelProfiles>();
        AIClients.AddImageGeneration(builder.Services, options);
        VoiceToTextHosting.Add(builder.Services, options);
        WebSearchHosting.Add(builder.Services, options);

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
}