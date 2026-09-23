using Microsoft.Extensions.AI;

namespace DigitalBrain.Discovery.Search;

internal sealed class AiCapabilityEmbedder : ICapabilityEmbedder
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly string _modelId;

    public AiCapabilityEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _modelId = generator.GetType().Name;
    }

    public string ModelId => _modelId;

    public async ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var generated = await _generator.GenerateAsync([text], cancellationToken: cancellationToken).ConfigureAwait(false);
        return generated[0].Vector.ToArray();
    }
}
