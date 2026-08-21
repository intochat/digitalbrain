using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

// Testing-mode counterpart of AIClients: Gemma resolves to one deterministic text responder.
internal static class AITestingClients
{
    private static readonly Type[] ModelKeys =
    [
        typeof(Gemma4),
    ];

    internal static void Add(IServiceCollection services)
    {
        var scriptedClient = new TestChatClient();
        foreach (var modelKey in ModelKeys)
        {
            services.AddKeyedSingleton<IChatClient>(modelKey, scriptedClient);
        }

        services.TryAddSingleton(static provider =>
            provider.GetRequiredKeyedService<IChatClient>(typeof(Gemma4)));
    }
}
