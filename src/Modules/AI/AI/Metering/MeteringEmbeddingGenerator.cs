using DigitalBrain.Contracts;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Metering;

/// <summary>
/// Records the provider's reported token classes for embedding calls made under an
/// <see cref="IntentContext"/>. Embeddings are a separate meter from chat, so they are recorded
/// with their own provider/model identity rather than folded into a chat entry.
/// </summary>
internal sealed class MeteringEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> innerGenerator, IIntentUsageSink sink, string provider, string model)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(innerGenerator)
{
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var embeddings = await base.GenerateAsync(values, options, cancellationToken).ConfigureAwait(false);
        if (IntentContext.Current is { } intent)
        {
            var usage = embeddings.Usage;
            var entry = new TokenUsageEntry(MeterKind.Embedding, provider, model,
                usage?.InputTokenCount, usage?.CachedInputTokenCount, usage?.ReasoningTokenCount,
                usage?.OutputTokenCount, usage?.TotalTokenCount, usage is not null, DateTimeOffset.UtcNow);
            try { await sink.RecordAsync(intent.IntentId, entry, cancellationToken).ConfigureAwait(false); }
            catch { /* Metering is an observer: a storage fault must never fail the embedding call. */ }
        }
        return embeddings;
    }
}