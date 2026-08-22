using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

public sealed class AIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.Equals(
                builder.Configuration[DigitalBrainNames.Mode],
                DigitalBrainNames.TestingMode,
                StringComparison.Ordinal))
        {
            AITestingClients.Add(builder.Services);
        }
        else
        {
            AIClients.Add(builder.Services);
            AIClients.AddImageGeneration(builder.Services, builder.Configuration);
        }

        VoiceToTextHosting.Add(builder.Services, builder.Configuration);
    }
}
