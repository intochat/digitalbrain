using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

// Testing-mode counterpart of AIClients: every model key resolves one shared corpus-scripted
// mock and embeddings are deterministic, so test hosts boot without Ollama/OpenAI containers.
// Deliberately absent: LlmWarmupHostedService and any Ollama/OpenAI configuration reads.
internal static class AITestingClients
{
    internal const string CorpusPathKey = $"{AIClients.ConfigurationRoot}:Corpus:Path";

    private static readonly Type[] ModelKeys =
    [
        typeof(Llama32),
        typeof(Gemma4),
        typeof(Qwen35),
        typeof(Granite41),
        typeof(Gpt56),
    ];

    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        var corpusPath = configuration[CorpusPathKey];
        if (string.IsNullOrWhiteSpace(corpusPath))
        {
            throw new InvalidOperationException(
                $"Testing mode requires {CorpusPathKey} to point at a directory of .feature "
                + "files scripting the mock LLM.");
        }

        // Loaded eagerly so a missing or malformed corpus fails host startup, not the first turn.
        var scriptedClient = new BddMockChatClient(BddScenarioCorpus.Load(corpusPath));
        foreach (var modelKey in ModelKeys)
        {
            services.AddKeyedSingleton<IChatClient>(modelKey, scriptedClient);
        }

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new DeterministicEmbeddingGenerator());

        // Mirrors AIModule's production default: the unkeyed IChatClient is the main model.
        services.TryAddSingleton(static provider =>
            provider.GetRequiredKeyedService<IChatClient>(typeof(Gemma4)));
    }
}
