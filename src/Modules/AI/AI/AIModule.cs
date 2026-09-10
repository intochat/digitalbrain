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

        builder.Services.TryAddSingleton(services =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var tools = new NativeTools();
            if (configuration.GetValue<bool>(TavilyWebSearch.EnabledConfigurationKey))
            {
                tools.Add("websearch", WebSearchFunction.Create(services.GetRequiredService<IWebSearch>()));
            }

            if (!string.IsNullOrWhiteSpace(configuration["DigitalBrain:Workspace:RepositoryPath"]))
            {
                tools.Add("repositorydiff", new RepositoryDiffFunction(configuration).Function);
            }

            return tools;
        });

        if (builder.Configuration[AIClients.DefaultModelKey] is { } defaultModel
            && !string.IsNullOrWhiteSpace(defaultModel))
        {
            builder.Services.TryAddSingleton(new AIDefaults(defaultModel));
        }
    }
}
