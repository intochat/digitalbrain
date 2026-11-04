using DigitalBrain.Contracts;

namespace DigitalBrain;

/// <summary>The deployed generated sources of one brain, at <c>{brain}/synapse.sources/default</c>.</summary>
[NeuronAlias("synapse.sources")]
public interface ISources
{
    /// <summary>Stores <paramref name="record"/>, replacing the record of its source key; journals <see cref="Deployed"/> when the hash is new.</summary>
    /// <param name="record">The record.</param>
    /// <param name="ct">Cancellation.</param>
    Task Put(SourceRecord record, CancellationToken ct);

    /// <summary>Records that a build of <paramref name="sourceKey"/> is staged; journals <see cref="Staged"/>.</summary>
    /// <param name="sourceKey">The source key.</param>
    /// <param name="hash">The staged build hash.</param>
    /// <param name="ct">Cancellation.</param>
    Task Stage(string sourceKey, string hash, CancellationToken ct);

    /// <summary>Removes the record of <paramref name="sourceKey"/> and journals <see cref="Undeployed"/>; an unknown key is ignored.</summary>
    /// <param name="sourceKey">The source key.</param>
    /// <param name="ct">Cancellation.</param>
    Task Remove(string sourceKey, CancellationToken ct);

    /// <summary>The record of <paramref name="sourceKey"/>, or null.</summary>
    /// <param name="sourceKey">The source key.</param>
    /// <param name="ct">Cancellation.</param>
    [ReadOnlyCall]
    Task<SourceRecord?> Get(string sourceKey, CancellationToken ct);

    /// <summary>Every record, ordered by source key.</summary>
    /// <param name="ct">Cancellation.</param>
    [ReadOnlyCall]
    Task<IReadOnlyList<SourceRecord>> List(CancellationToken ct);
}

/// <summary>The state of the sources neuron: one record per deployed source key.</summary>
/// <param name="Records">The records.</param>
public sealed record SourcesState(IReadOnlyList<SourceRecord> Records)
{
    /// <summary>No sources.</summary>
    public SourcesState() : this([]) { }
}

/// <summary>The sources neuron of a brain: keeps the deployed generated sources with their builds, so a host can restore them.</summary>
[Neuron("synapse.sources")]
public sealed class SourcesNeuron : Neuron<SourcesState>, ISources
{
    /// <inheritdoc />
    public Task Put(SourceRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var existing = Find(record.SourceKey);
        var next = State with { Records = [.. State.Records.Where(r => r.SourceKey != record.SourceKey), record] };
        return existing is not null && existing.Hash == record.Hash
            ? Save(next, [])
            : Save(next, new Deployed(record.SourceKey, record.Hash, record.Aliases));
    }

    /// <inheritdoc />
    public Task Stage(string sourceKey, string hash, CancellationToken ct) => Save(State, new Staged(sourceKey, hash));

    /// <inheritdoc />
    public Task Remove(string sourceKey, CancellationToken ct)
    {
        if (Find(sourceKey) is null) { return Task.CompletedTask; }
        return Save(State with { Records = [.. State.Records.Where(r => r.SourceKey != sourceKey)] }, new Undeployed(sourceKey));
    }

    /// <inheritdoc />
    public Task<SourceRecord?> Get(string sourceKey, CancellationToken ct) => Task.FromResult(Find(sourceKey));

    /// <inheritdoc />
    public Task<IReadOnlyList<SourceRecord>> List(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SourceRecord>>([.. State.Records.OrderBy(r => r.SourceKey, StringComparer.Ordinal)]);

    private SourceRecord? Find(string sourceKey) => State.Records.FirstOrDefault(r => r.SourceKey == sourceKey);
}
