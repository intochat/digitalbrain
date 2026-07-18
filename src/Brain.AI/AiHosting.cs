namespace DigitalBrain.AI;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling.Json;

public static class AiHosting
{
    public static ISiloBuilder AddBrainAI(this ISiloBuilder silo, Action<AiProviderOptions>? configure = null)
    {
        silo.UseJsonJournalFormat(AiJournalJsonContext.Default);
        silo.Services.AddOptions<AiProviderOptions>();
        if (configure is not null)
            silo.Services.Configure(configure);

        return silo;
    }

    public static ISiloBuilder AddBrainAIChatClient(
        this ISiloBuilder silo,
        string serviceKey,
        IChatClient chatClient)
    {
        silo.Services.AddKeyedSingleton(serviceKey, chatClient);
        return silo;
    }
}
