using DigitalBrain.Abstractions;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        }

        VoiceToTextHosting.Add(builder.Services, builder.Configuration);

        // The unkeyed IChatClient IS the main model. Every other model use is an
        // explicit keyed choice (ask_llama).
        builder.Services.TryAddSingleton(static services =>
            services.GetRequiredKeyedService<IChatClient>(typeof(Gemma4)));
    }
}

