using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

// Testing-mode counterpart of AIClients: every model key resolves one deterministic text
// responder, so test hosts boot without Ollama/OpenAI containers.
internal static class AITestingClients
{
    private static readonly Type[] ModelKeys =
    [
        typeof(Llama32),
        typeof(Gemma4),
        typeof(Qwen35),
        typeof(Granite41),
        typeof(Gpt56),
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
