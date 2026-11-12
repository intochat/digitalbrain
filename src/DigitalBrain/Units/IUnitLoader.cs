namespace DigitalBrain.Units;

/// <summary>
/// Tenant-free content-addressed unit loader: loads compiled unit code into collectible contexts keyed by <see cref="BuildRef"/>.
/// </summary>
public interface IUnitLoader
{
    /// <summary>Acquires a handle if the build is already loaded; returns null otherwise.</summary>
    UnitHandle? TryAcquire(BuildRef build);

    /// <summary>Loads or acquires an existing handle for the given build image.</summary>
    UnitHandle Acquire(BuildImage image);

    /// <summary>Builds whose context was unloaded and is still reachable in memory.</summary>
    IReadOnlyList<BuildRef> Lingering();

    /// <summary>Looks up the catalog for a state record from its own declaring assembly.</summary>
    SynapseCatalog? CatalogOf(Type stateRecord);

    /// <summary>Raised once per initial load with the build's [Signal] types: the host maps them.</summary>
    event Action<BuildRef, IReadOnlyList<Type>>? Loaded;

    /// <summary>Raised once when refcount reaches zero with the build's [Signal] types: the host unmaps them before unload.</summary>
    event Action<BuildRef, IReadOnlyList<Type>>? Unloading;
}
