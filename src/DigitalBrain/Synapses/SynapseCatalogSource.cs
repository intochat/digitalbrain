namespace DigitalBrain.Units;

/// <summary>
/// Thread-safe <see cref="ISynapseCatalogSource"/> providing the static framework catalog and delegating dynamic state record lookups to <see cref="IUnitLoader"/>.
/// </summary>
public sealed class SynapseCatalogSource : ISynapseCatalogSource
{
    private readonly Lock _gate = new();
    private SynapseCatalog _current;
    private readonly Func<IUnitLoader?>? _units;

    /// <summary>Creates a catalog source without a dynamic unit loader.</summary>
    public SynapseCatalogSource(SynapseCatalog initial)
        : this(initial, (Func<IUnitLoader?>?)null)
    {
    }

    /// <summary>Creates a catalog source with an optional unit loader.</summary>
    public SynapseCatalogSource(SynapseCatalog initial, IUnitLoader? units)
        : this(initial, units is null ? null : () => units)
    {
    }

    /// <summary>Creates a catalog source with a lazy unit loader factory.</summary>
    public SynapseCatalogSource(SynapseCatalog initial, Func<IUnitLoader?>? units)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
        _units = units;
    }

    /// <inheritdoc />
    public SynapseCatalog Static => Volatile.Read(ref _current);

    /// <inheritdoc />
    public SynapseCatalog Of(Type stateRecord)
    {
        ArgumentNullException.ThrowIfNull(stateRecord);
        return _units?.Invoke()?.CatalogOf(stateRecord) ?? Static;
    }

    /// <summary>Adds the states and synapses of <paramref name="catalog"/>.</summary>
    public void Merge(SynapseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        lock (_gate) { _current = _current.Merge(catalog); }
    }

    /// <summary>Drops <paramref name="alias"/> and every alias of <paramref name="catalog"/>, then merges <paramref name="catalog"/>.</summary>
    public void Replace(string alias, SynapseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(catalog);
        lock (_gate) { _current = _current.Without(alias).Replace(catalog); }
    }

    /// <summary>Drops the state <paramref name="alias"/> and its synapses.</summary>
    public void Remove(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        lock (_gate) { _current = _current.Without(alias); }
    }
}
