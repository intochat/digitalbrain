using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

public sealed class AIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!string.IsNullOrWhiteSpace(builder.Configuration["DigitalBrain:Workspace:RepositoryPath"]))
        {
            builder.Services.AddSingleton<IAgentToolSource>(new RepositoryDiffToolSource(builder.Configuration));
        }

        if (string.Equals(
                builder.Configuration[DigitalBrainNames.Mode],
                DigitalBrainNames.TestingMode,
                StringComparison.Ordinal))
        {
            AITestingClients.Add(builder.Services);
            return;
        }

        AIClients.Add(builder.Services);
        AIClients.AddImageGeneration(builder.Services, builder.Configuration);
        VoiceToTextHosting.Add(builder.Services, builder.Configuration);
    }
}
