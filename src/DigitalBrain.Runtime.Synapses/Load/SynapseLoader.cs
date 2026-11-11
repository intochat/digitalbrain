using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DigitalBrain.Units;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>Loads compiled synapse assemblies into collectible contexts and verifies that they unload.</summary>
public sealed class SynapseLoader
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string SourceKey, string Hash), List<WeakReference<SynapseLoadContext>>> _contexts = [];
    private readonly Dictionary<(string SourceKey, string Hash), WeakReference<LoadedSource>> _live = [];
    private readonly ConditionalWeakTable<SynapseLoadContext, object> _detached = [];

    /// <summary>Loads a successful build into a new collectible context and scans it.</summary>
    /// <param name="build">The build; <see cref="SynapseBuild.Success"/> must be true.</param>
    /// <returns>The loaded source.</returns>
    /// <exception cref="InvalidOperationException">The build failed, or a live context for the same source key and hash is already registered (the loader never loads one pair twice).</exception>
    public LoadedSource Load(SynapseBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (!build.Success)
        {
            throw new InvalidOperationException("Cannot load a failed build.");
        }

        var sourceKey = SourceKeyOf(build.AssemblyName);
        lock (_gate)
        {
            Prune();
            if (HasLive(sourceKey, build.Hash))
            {
                throw new InvalidOperationException($"A context for '{sourceKey}' with hash {build.Hash} is already loaded; unload it first.");
            }
        }

        var context = new SynapseLoadContext(build.AssemblyName);
        try
        {
            using var image = new MemoryStream(build.Assembly, writable: false);
            using var symbols = new MemoryStream(build.Symbols, writable: false);
            var assembly = context.LoadFromStream(image, symbols);
            var catalog = SynapseCatalog.Scan(assembly);
            IReadOnlyList<Type> signals =
            [
                .. assembly.GetTypes().Where(t => !t.IsAbstract && typeof(Signal).IsAssignableFrom(t) && t.GetCustomAttribute<SignalAttribute>() is not null),
            ];
            IReadOnlyList<Type> states = [.. catalog.States.Values];
            lock (_gate)
            {
                if (HasLive(sourceKey, build.Hash))
                {
                    throw new InvalidOperationException($"A context for '{sourceKey}' with hash {build.Hash} is already loaded; unload it first.");
                }

                if (!_contexts.TryGetValue((sourceKey, build.Hash), out var list))
                {
                    _contexts[(sourceKey, build.Hash)] = list = [];
                }

                list.Add(new WeakReference<SynapseLoadContext>(context));
                var loaded = new LoadedSource(sourceKey, build.Hash, assembly, catalog, signals, states);
                _live[(sourceKey, build.Hash)] = new WeakReference<LoadedSource>(loaded);
                return loaded;
            }
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    /// <summary>
    /// Unloads the context of <paramref name="loaded"/>, drops the loader's strong references, and collects until the context dies or <paramref name="gcRounds"/> is spent.
    /// The record's <see cref="LoadedSource.Assembly"/>, catalog and types pin the context, so the caller must drop every reference to the record, and to anything reached through it, before awaiting the result.
    /// </summary>
    /// <param name="loaded">The loaded source.</param>
    /// <param name="gcRounds">The most collections to try.</param>
    /// <returns>True when the context was collected; false when something still holds it. A false result can be retried with <see cref="RetryUnload(string, string, int)"/>.</returns>
    public Task<bool> Unload(LoadedSource loaded, int gcRounds = 10)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var weak = Detach(loaded);
        return CollectOne(weak, gcRounds);
    }

    /// <summary>Retries the collection of a source (the loader holds at most one live context per key and hash) whose earlier <see cref="Unload(LoadedSource, int)"/> reported a leak, without needing the record.</summary>
    /// <param name="sourceKey">The source key.</param>
    /// <param name="hash">The build hash.</param>
    /// <param name="gcRounds">The most collections to try.</param>
    /// <returns>True when every context of that source and hash is collected.</returns>
    public Task<bool> RetryUnload(string sourceKey, string hash, int gcRounds = 10)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(hash);
        List<WeakReference<SynapseLoadContext>> pending;
        lock (_gate)
        {
            pending = _contexts.TryGetValue((sourceKey, hash), out var list) ? [.. list] : [];
        }

        foreach (var weak in pending)
        {
            if (weak.TryGetTarget(out var context))
            {
                DigitalBrain.Invocation.RuntimeProtocol.Forget(context);
                context.Unload();
            }
        }

        return CollectAll(pending, gcRounds);
    }

    // The loaded source of a live (not unloading) context of the key and hash, when its record is still reachable.
    internal bool TryGetLive(string sourceKey, string hash, out LoadedSource? loaded)
    {
        lock (_gate)
        {
            loaded = null;
            return _live.TryGetValue((sourceKey, hash), out var weak) && weak.TryGetTarget(out loaded)
                && !_detached.TryGetValue((SynapseLoadContext)AssemblyLoadContext.GetLoadContext(loaded.Assembly)!, out _);
        }
    }

    // A context of the pair that is not unloading; an unloading one may still be collecting and does not block a new load.
    private bool HasLive(string sourceKey, string hash) =>
        _contexts.TryGetValue((sourceKey, hash), out var list)
        && list.Exists(w => w.TryGetTarget(out var context) && !_detached.TryGetValue(context, out _));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<SynapseLoadContext> Detach(LoadedSource loaded)
    {
        var context = AssemblyLoadContext.GetLoadContext(loaded.Assembly) as SynapseLoadContext
            ?? throw new ArgumentException("The source was not loaded by a SynapseLoader.", nameof(loaded));
        lock (_gate)
        {
            var weak = _contexts.TryGetValue((loaded.SourceKey, loaded.Hash), out var list)
                ? list.Find(w => w.TryGetTarget(out var c) && ReferenceEquals(c, context))
                : null;
            weak ??= new WeakReference<SynapseLoadContext>(context);
            _detached.AddOrUpdate(context, new object());
            _ = _live.Remove((loaded.SourceKey, loaded.Hash));
            DigitalBrain.Invocation.RuntimeProtocol.Forget(context);
            context.Unload();
            return weak;
        }
    }

    private async Task<bool> CollectOne(WeakReference<SynapseLoadContext> weak, int gcRounds)
    {
        // Yield first so the caller's frame, which in an unoptimised build still names the record, is gone before the first collection.
        await Task.Yield();
        var dead = Collected(weak, gcRounds);
        Prune();
        return dead;
    }

    private async Task<bool> CollectAll(List<WeakReference<SynapseLoadContext>> weaks, int gcRounds)
    {
        await Task.Yield();
        var dead = true;
        foreach (var weak in weaks)
        {
            dead &= Collected(weak, gcRounds);
        }

        Prune();
        return dead;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Collected(WeakReference<SynapseLoadContext> weak, int gcRounds)
    {
        for (var i = 0; i < gcRounds; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (IsDead(weak))
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return IsDead(weak);
    }

    // Its own frame: an unoptimised JIT keeps the hidden out-local alive for the whole method, which would pin the context for every later round.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsDead(WeakReference<SynapseLoadContext> weak) => !weak.TryGetTarget(out _);

    private void Prune()
    {
        lock (_gate)
        {
            foreach (var key in _contexts.Keys.ToArray())
            {
                _contexts[key].RemoveAll(IsDead);
                if (_contexts[key].Count == 0)
                {
                    _contexts.Remove(key);
                }
            }
        }
    }

    // The compiler names assemblies "synapse.<SourceKey>.<hash8>".
    private static string SourceKeyOf(string assemblyName)
    {
        const string prefix = "synapse.";
        var end = assemblyName.LastIndexOf('.');
        return assemblyName.StartsWith(prefix, StringComparison.Ordinal) && end > prefix.Length
            ? assemblyName[prefix.Length..end]
            : assemblyName;
    }
}
