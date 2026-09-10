using DigitalBrain.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Tests;

// The scripted client is the default provider, so nothing in a scenario names a model.
internal static class ScriptedAi
{
    internal static Action<ISiloBuilder> Configure(BrainWorld world)
        => silo =>
        {
            silo.Services.AddKeyedSingleton<IChatClient>("scripted", (_, _) => world.Scripted);
            silo.Services.AddSingleton(new AIDefaults("scripted"));
        };
}
