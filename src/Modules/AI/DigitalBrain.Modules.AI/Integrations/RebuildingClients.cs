using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class RebuildingChatClient(Func<IChatClient> create, Func<long> generation) : IChatClient
{
    private readonly Lock _gate = new();
    private IChatClient? _inner;
    private long _builtAt = -1;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Current().GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Current().GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => _inner?.Dispose();

    private IChatClient Current()
    {
        lock (_gate)
        {
            var now = generation();
            if (_inner is null || now != _builtAt)
            {
                _inner?.Dispose();
                _inner = null;
                _inner = create();
                _builtAt = now;
            }

            return _inner;
        }
    }
}

internal sealed class RebuildingEmbeddingGenerator(Func<IEmbeddingGenerator<string, Embedding<float>>> create, Func<long> generation)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Lock _gate = new();
    private IEmbeddingGenerator<string, Embedding<float>>? _inner;
    private long _builtAt = -1;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        => Current().GenerateAsync(values, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => _inner?.Dispose();

    private IEmbeddingGenerator<string, Embedding<float>> Current()
    {
        lock (_gate)
        {
            var now = generation();
            if (_inner is null || now != _builtAt)
            {
                _inner?.Dispose();
                _inner = null;
                _inner = create();
                _builtAt = now;
            }

            return _inner;
        }
    }
}
