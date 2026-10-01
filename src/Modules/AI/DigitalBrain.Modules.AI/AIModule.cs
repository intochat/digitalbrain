using DigitalBrain.AI.Web;
using DigitalBrain.AI.Media;
using DigitalBrain.AI.WebSearch;
using DigitalBrain.AI.Agents;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.AI;

[ModuleDeployment("DigitalBrain.AI.AIDeployment, DigitalBrain.Modules.AI.Deployment")]
public sealed class AIModule : IModule<AIOptions>
{
    public const string McpServersKey = "DigitalBrain:AI:McpServers";

    public static IntegrationDefinition[] Integrations => AiIntegrations.Definitions;

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AIOptions.Register(builder.Services);
        var options = AIOptions.Read(builder.Configuration);
        var workspace = builder.Configuration.GetSection(AIWorkspaceOptions.SectionName).Get<AIWorkspaceOptions>() ?? new();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IAiCredentials, RegistrationCredentials>();
        AIClients.Add(builder.Services);
        builder.Services.TryAddSingleton<Agents.IAgentTurnRunner, Agents.AgentTurnRunner>();
        builder.Services.TryAddSingleton<ModelProfiles>();
        builder.Services.AddSingleton<ICapabilitySource, AiModelCapabilities>();
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
