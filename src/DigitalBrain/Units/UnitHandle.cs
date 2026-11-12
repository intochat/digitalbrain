namespace DigitalBrain.Units;

/// <summary>
/// A refcounted reference to an active unit build in a collectible load context.
/// </summary>
public sealed class UnitHandle : IDisposable
{
    private Action<BuildRef>? _release;

    /// <summary>The build reference.</summary>
    public BuildRef Build { get; }

    /// <summary>The synapse catalog exposed by this build.</summary>
    public SynapseCatalog Catalog { get; }

    /// <summary>Creates a new unit handle.</summary>
    public UnitHandle(BuildRef build, SynapseCatalog catalog, Action<BuildRef>? release)
    {
        Build = build;
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _release = release;
    }

    /// <summary>Creates a handle wrapping a static catalog without refcounting.</summary>
    public static UnitHandle Static(SynapseCatalog catalog) =>
        new(new BuildRef("static", string.Empty), catalog, null);

    /// <summary>Looks up the state record type for the specified alias.</summary>
    public Type StateOf(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return Catalog.States.TryGetValue(alias, out var state)
            ? state
            : throw new KeyNotFoundException($"Alias '{alias}' is not a state record of build '{Build.SourceKey}'.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke(Build);
    }
}
