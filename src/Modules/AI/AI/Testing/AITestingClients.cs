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
        typeof(IGemma4),
    ];

    internal static void Add(IServiceCollection services)
    {
        var scriptedClient = new TestChatClient();
        var embeddingGenerator = new TestEmbeddingGenerator();
        foreach (var modelKey in ModelKeys)
        {
            services.AddKeyedSingleton<IChatClient>(modelKey, scriptedClient);
        }

        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            typeof(IEmbeddingGemma),
            embeddingGenerator);

        services.TryAddSingleton(static provider =>
            provider.GetRequiredKeyedService<IChatClient>(typeof(IGemma4)));
    }

    private sealed class TestEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embeddings = values.Select(value =>
                new Embedding<float>(
                    new[] { value.Length, value.Aggregate(0f, (sum, character) => sum + character) }));
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
