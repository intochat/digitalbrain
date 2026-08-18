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

        // A blank path boots test mode with no scripted scenarios rather than refusing to
        // start -- a host that never talks to the LLM (e.g. an E2E health-check smoke) should
        // not need a corpus wired. A path that IS set but broken (missing/empty/unparseable
        // directory) still fails host startup eagerly below -- that stays a real corpus
        // author's mistake to catch fast, not the first turn. Either way, the first prompt
        // that actually reaches an empty-corpus mock throws MockLlmMissException naming the
        // config key to set, loud at the point it matters.
        var scriptedClient = new BddMockChatClient(string.IsNullOrWhiteSpace(corpusPath)
            ? BddScenarioCorpus.Empty()
            : BddScenarioCorpus.Load(corpusPath));
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
