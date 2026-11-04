using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>Loads compiled synapse assemblies into collectible contexts and verifies that they unload.</summary>
public sealed class SynapseLoader
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string SourceKey, string Hash), List<WeakReference<SynapseLoadContext>>> _contexts = [];

    /// <summary>Loads a successful build into a new collectible context and scans it.</summary>
    /// <param name="build">The build; <see cref="SynapseBuild.Success"/> must be true.</param>
    /// <returns>The loaded source.</returns>
    /// <exception cref="InvalidOperationException">The build failed.</exception>
    public LoadedSource Load(SynapseBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (!build.Success)
        {
            throw new InvalidOperationException("Cannot load a failed build.");
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
            var sourceKey = SourceKeyOf(build.AssemblyName);

            lock (_gate)
            {
                var key = (sourceKey, build.Hash);
                if (!_contexts.TryGetValue(key, out var list))
                {
                    _contexts[key] = list = [];
                }

                list.Add(new WeakReference<SynapseLoadContext>(context));
            }

            return new LoadedSource(sourceKey, build.Hash, assembly, catalog, signals, states);
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

    /// <summary>Retries the collection of a source whose earlier <see cref="Unload(LoadedSource, int)"/> reported a leak, without needing the record.</summary>
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
                context.Unload();
            }
        }

        return CollectAll(pending, gcRounds);
    }

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
            if (!weak.TryGetTarget(out _))
            {
                return true;
            }
        }

        return !weak.TryGetTarget(out _);
    }

    private void Prune()
    {
        lock (_gate)
        {
            foreach (var key in _contexts.Keys.ToArray())
            {
                _contexts[key].RemoveAll(w => !w.TryGetTarget(out _));
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
