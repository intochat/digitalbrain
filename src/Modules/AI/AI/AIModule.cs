using DigitalBrain.AI.WebSearch;
using DigitalBrain.AI.Web;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI;

public sealed class AIModule : IModule
{
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
}
