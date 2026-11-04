namespace DigitalBrain.Runtime.Synapses;

/// <summary>An <see cref="ISourceStore"/> over the <see cref="ISources"/> neuron of each brain.</summary>
/// <param name="sources">The sources neuron of a brain (<c>{brain}/synapse.sources/default</c>).</param>
public sealed class NeuronSourceStore(Func<BrainId, ISources> sources) : ISourceStore
{
    private readonly Func<BrainId, ISources> sources = sources ?? throw new ArgumentNullException(nameof(sources));

    /// <inheritdoc />
    public Task<SourceRecord?> Get(BrainId brain, string sourceKey, CancellationToken ct) => sources(brain).Get(sourceKey, ct);

    /// <inheritdoc />
    public Task Put(BrainId brain, SourceRecord record, CancellationToken ct) => sources(brain).Put(record, ct);

    /// <inheritdoc />
    public Task Stage(BrainId brain, string sourceKey, string hash, CancellationToken ct) => sources(brain).Stage(sourceKey, hash, ct);

    /// <inheritdoc />
    public Task Remove(BrainId brain, string sourceKey, CancellationToken ct) => sources(brain).Remove(sourceKey, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<SourceRecord>> List(BrainId brain, CancellationToken ct) => sources(brain).List(ct);
}
