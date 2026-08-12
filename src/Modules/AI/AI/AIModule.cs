using DigitalBrain.AI.Ollama;
using DigitalBrain.Modules.Sdk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

public sealed class AIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
        AIClients.Add(builder.Services);

        // The unkeyed IChatClient IS the main model. Every other model use is an
        // explicit keyed choice (ask_llama, convene_model_team).
        builder.Services.TryAddSingleton(static services =>
            services.GetRequiredKeyedService<IChatClient>(typeof(Gemma4)));
    }
}
