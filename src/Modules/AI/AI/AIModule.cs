using DigitalBrain.AI.WebSearch;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

public sealed class AIModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AIClients.Add(builder.Services);
        AIClients.AddImageGeneration(builder.Services, builder.Configuration);
        VoiceToTextHosting.Add(builder.Services, builder.Configuration);
        WebSearchHosting.Add(builder.Services, builder.Configuration);

        builder.Services.TryAddSingleton<NativeTools>();
        if (builder.Configuration.GetValue<bool>(TavilyWebSearch.EnabledConfigurationKey))
        {
            builder.Services.AddNativeTool("websearch", services => WebSearchFunction.Create(services.GetRequiredService<IWebSearch>()));
        }

        if (!string.IsNullOrWhiteSpace(builder.Configuration["DigitalBrain:Workspace:RepositoryPath"]))
        {
            builder.Services.AddNativeTool("repositorydiff", services => new RepositoryDiffFunction(services.GetRequiredService<IConfiguration>()).Function);
        }

        if (builder.Configuration[AIClients.DefaultModelKey] is { } defaultModel
            && !string.IsNullOrWhiteSpace(defaultModel))
        {
            builder.Services.TryAddSingleton(new AIDefaults(defaultModel));
        }
    }
}
