using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Invocation;
using DigitalBrain.Units;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// Loaded unit code by content: one collectible context per (source key, hash), shared by every brain that runs that build,
/// alive while a handle references it. The allow-list is checked here, at the only door from bytes to an Assembly. Knows no brain.
/// </summary>
public sealed class UnitLoader : IUnitLoader
{
    private readonly Lock _gate = new();
    private readonly Dictionary<BuildRef, Slot> _live = [];
    private readonly List<(BuildRef Build, WeakReference Context)> _unloading = [];
    private readonly ConditionalWeakTable<Assembly, SynapseCatalog> _catalogs = [];

    /// <summary>Raised once per load with the build's [Signal] types: the host maps them.</summary>
    public event Action<BuildRef, IReadOnlyList<Type>>? Loaded;

    /// <summary>Raised once when refcount reaches zero with the build's [Signal] types: the host unmaps them before unload.</summary>
    public event Action<BuildRef, IReadOnlyList<Type>>? Unloading;

    /// <summary>
    /// Looks up the catalog for a state record from its own declaring assembly.
    /// Weak table ensures contexts are never pinned.
    /// </summary>
    public SynapseCatalog? CatalogOf(Type stateRecord)
    {
        ArgumentNullException.ThrowIfNull(stateRecord);
        return stateRecord.IsCollectible && _catalogs.TryGetValue(stateRecord.Assembly, out var catalog)
            ? catalog
            : null;
    }

    /// <summary>Acquires a handle if the build is already loaded; returns null otherwise.</summary>
    public UnitHandle? TryAcquire(BuildRef build)
    {
        lock (_gate)
        {
            if (_live.TryGetValue(build, out var slot))
            {
                slot.Count++;
                return new UnitHandle(build, slot.Catalog, Release);
            }
            return null;
        }
    }

    /// <summary>Loads or acquires an existing handle for the given build image.</summary>
    public UnitHandle Acquire(BuildImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (TryAcquire(image.Ref) is { } held)
        {
            return held;
        }

        // Allow-list boundary check occurs BEFORE any assembly load
        if (AllowedReferences.FirstDisallowed(image.Assembly) is { } disallowed)
        {
            throw new UnitRefusedException(image.Ref, disallowed);
        }

        var context = new SynapseLoadContext(image.AssemblyName);
        Assembly assembly;
        SynapseCatalog catalog;
        IReadOnlyList<Type> signals;

        try
        {
            using var imageStream = new MemoryStream(image.Assembly, writable: false);
            using var symbolsStream = new MemoryStream(image.Symbols, writable: false);
            assembly = context.LoadFromStream(imageStream, symbolsStream);
            catalog = SynapseCatalog.Scan(assembly);
            signals =
            [
                .. assembly.GetTypes().Where(t => !t.IsAbstract && typeof(Signal).IsAssignableFrom(t) && t.GetCustomAttribute<SignalAttribute>() is not null),
            ];
        }
        catch
        {
            context.Unload();
            throw;
        }

        Slot slot;
        bool wonRace = false;
        lock (_gate)
        {
            if (_live.TryGetValue(image.Ref, out var existing))
            {
                existing.Count++;
                slot = existing;
            }
            else
            {
                slot = new Slot(context, catalog, signals);
                _live[image.Ref] = slot;
                _catalogs.Add(assembly, catalog);
                wonRace = true;
            }
        }

        if (!wonRace)
        {
            // Lost race: another thread loaded and registered the build concurrently
            RuntimeProtocol.Forget(context);
            context.Unload();
            return new UnitHandle(image.Ref, slot.Catalog, Release);
        }

        Loaded?.Invoke(image.Ref, signals);
        return new UnitHandle(image.Ref, slot.Catalog, Release);
    }

    /// <summary>Builds whose context was unloaded and is still reachable in memory.</summary>
    public IReadOnlyList<BuildRef> Lingering()
    {
        lock (_gate)
        {
            _unloading.RemoveAll(entry => !entry.Context.IsAlive);
            return [.. _unloading.Select(entry => entry.Build)];
        }
    }

    private void Release(BuildRef build)
    {
        Slot? last = null;
        lock (_gate)
        {
            if (_live.TryGetValue(build, out var slot) && --slot.Count == 0)
            {
                _live.Remove(build);
                _unloading.Add((build, new WeakReference(slot.Context)));
                last = slot;
            }
        }

        if (last is null)
        {
            return;
        }

        // Notify outside the lock so the host unmaps signal types before context unloading
        Unloading?.Invoke(build, last.Signals);
        RuntimeProtocol.Forget(last.Context);
        last.Context.Unload();
    }

    private sealed class Slot(SynapseLoadContext context, SynapseCatalog catalog, IReadOnlyList<Type> signals)
    {
        public SynapseLoadContext Context { get; } = context;
        public SynapseCatalog Catalog { get; } = catalog;
        public IReadOnlyList<Type> Signals { get; } = signals;
        public int Count { get; set; } = 1;
    }
}
