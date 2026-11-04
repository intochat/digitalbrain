namespace DigitalBrain;

/// <summary>
/// A thread-safe <see cref="ISynapseCatalogSource"/>: changes are serialized under a lock and <see cref="Current"/> is a volatile read of
/// an immutable catalog. <see cref="Merge"/>, <see cref="Replace"/> and <see cref="Remove"/> bump <see cref="Version"/> and raise
/// <see cref="Changed"/>; staging never changes <see cref="Current"/>.
/// </summary>
/// <param name="initial">The catalog in force at start.</param>
public sealed class SynapseCatalogSource(SynapseCatalog initial) : ISynapseCatalogSource
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SynapseCatalog> _staged = new(StringComparer.Ordinal);
    private SynapseCatalog _current = initial ?? throw new ArgumentNullException(nameof(initial));
    private long _version;

    /// <inheritdoc />
    public SynapseCatalog Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public long Version => Interlocked.Read(ref _version);

    /// <inheritdoc />
    public event Action<SynapseCatalog>? Changed;

    /// <summary>Adds the states and synapses of <paramref name="catalog"/>.</summary>
    /// <param name="catalog">The catalog to add.</param>
    /// <exception cref="SynapseShapeException">A state alias is already in <see cref="Current"/>.</exception>
    public void Merge(SynapseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Publish(current => current.Merge(catalog));
    }

    /// <summary>Drops <paramref name="alias"/> and every alias of <paramref name="catalog"/>, then merges <paramref name="catalog"/>.</summary>
    /// <param name="alias">The state alias being replaced.</param>
    /// <param name="catalog">The catalog that replaces it.</param>
    public void Replace(string alias, SynapseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(catalog);
        Publish(current => current.Without(alias).Replace(catalog));
    }

    /// <summary>Drops the state <paramref name="alias"/> and its synapses.</summary>
    /// <param name="alias">The state alias.</param>
    public void Remove(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        Publish(current => current.Without(alias));
    }

    /// <summary>Stages <paramref name="staged"/> under <paramref name="sourceKey"/> for <see cref="ForNeeds"/>, replacing an earlier stage of the key.</summary>
    /// <param name="sourceKey">The key of the stage, such as an upload id.</param>
    /// <param name="staged">The catalog to serve for the aliases it declares.</param>
    public void Stage(string sourceKey, SynapseCatalog staged)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(staged);
        lock (_gate) { _staged[sourceKey] = staged; }
    }

    /// <summary>Drops the stage of <paramref name="sourceKey"/>; an unknown key is ignored.</summary>
    /// <param name="sourceKey">The key of the stage.</param>
    public void Unstage(string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        lock (_gate) { _staged.Remove(sourceKey); }
    }

    /// <inheritdoc />
    public SynapseCatalog ForNeeds(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        lock (_gate)
        {
            return _staged.Values.LastOrDefault(staged => staged.States.ContainsKey(alias)) ?? _current;
        }
    }

    private void Publish(Func<SynapseCatalog, SynapseCatalog> change)
    {
        SynapseCatalog next;
        lock (_gate)
        {
            next = change(_current);
            Volatile.Write(ref _current, next);
            Interlocked.Increment(ref _version);
        }

        Changed?.Invoke(next);
    }
}
