using System.Runtime.CompilerServices;
using DigitalBrain.Spi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// Deploys, updates and retires generated synapse sources: compiles, loads into a collectible context, makes the source live through the
/// registrar and installs its aliases. A version that changes the needs of an installed alias is staged (its catalog serves
/// <see cref="ISynapseCatalogSource.ForNeeds"/> only) until every changed alias is <see cref="Installed"/>; an <see cref="InstallDenied"/>
/// drops it and the old version stays live. Per brain and source key it keeps the live and the staged <see cref="LoadedSource"/> in memory.
/// </summary>
public sealed partial class SynapseHost : ISynapseHost, IAsyncDisposable
{
    private const string ListenerName = "synapse.host";

    private static readonly IReadOnlyDictionary<string, string> NoBindings = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly SynapseCompiler compiler;
    private readonly SynapseLoader loader;
    private readonly ISynapseRegistrar registrar;
    private readonly ISynapseCatalogSource catalog;
    private readonly Func<BrainId, IInstalls> installs;
    private readonly ISourceStore store;
    private readonly IClusterListeners listeners;
    private readonly ILogger<SynapseHost> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<(BrainId Brain, string SourceKey), Entry> entries = [];
    private readonly List<(string SourceKey, string Hash)> leaked = [];
    private readonly List<IAsyncDisposable> registrations = [];
    private bool listening;

    /// <summary>Creates a host.</summary>
    /// <param name="compiler">Compiles the sources.</param>
    /// <param name="loader">Loads and unloads the builds.</param>
    /// <param name="registrar">Makes a loaded source live and removes it.</param>
    /// <param name="catalog">The live catalog source; staged catalogs are staged on it.</param>
    /// <param name="installs">The installs neuron of a brain.</param>
    /// <param name="store">Where the live sources are kept.</param>
    /// <param name="listeners">The cluster listeners the host learns of <see cref="Installed"/> and <see cref="InstallDenied"/> from.</param>
    /// <param name="logger">Logs unloads that did not collect; optional.</param>
    public SynapseHost(
        SynapseCompiler compiler,
        SynapseLoader loader,
        ISynapseRegistrar registrar,
        ISynapseCatalogSource catalog,
        Func<BrainId, IInstalls> installs,
        ISourceStore store,
        IClusterListeners listeners,
        ILogger<SynapseHost>? logger = null)
    {
        this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.installs = installs ?? throw new ArgumentNullException(nameof(installs));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.listeners = listeners ?? throw new ArgumentNullException(nameof(listeners));
        this.logger = logger ?? NullLogger<SynapseHost>.Instance;
    }

    /// <summary>The source key and hash of every context an unload or retire could not collect.</summary>
    public IReadOnlyList<(string SourceKey, string Hash)> Leaked
    {
        get
        {
            lock (leaked) { return [.. leaked]; }
        }
    }

    /// <inheritdoc />
    public async Task<DeployOutcome> Deploy(BrainId brain, SynapseSource source, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindingsPerAlias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bindingsPerAlias);
        var build = compiler.Compile(source);
        if (!build.Success)
        {
            return new DeployOutcome(DeployStatus.Failed, build.Hash, build.Diagnostics, Empty(), "the source does not compile");
        }

        await Listen().ConfigureAwait(false);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            entries.TryGetValue((brain, source.SourceKey), out var entry);
            if (entry is not null && string.Equals(entry.Live.Hash, build.Hash, StringComparison.Ordinal))
            {
                return new DeployOutcome(DeployStatus.Unchanged, build.Hash, build.Diagnostics, Empty(), null);
            }

            if (entry?.Staged is { } staged && string.Equals(staged.Loaded.Hash, build.Hash, StringComparison.Ordinal))
            {
                return new DeployOutcome(DeployStatus.Staged, build.Hash, build.Diagnostics, Empty(), "this version is already staged");
            }

            if (entry?.Staged is not null)
            {
                await DropStaged(entry).ConfigureAwait(false);
            }

            LoadedSource loaded;
            try
            {
                loaded = loader.Load(build);
            }
            catch (Exception error) when (error is InvalidOperationException or BadImageFormatException or System.Reflection.ReflectionTypeLoadException or SynapseShapeException)
            {
                return new DeployOutcome(DeployStatus.Failed, build.Hash, build.Diagnostics, Empty(), $"the build does not load: {error.Message}");
            }

            var aliases = AliasesOf(loaded);
            if (Owner(brain, source.SourceKey, aliases) is { } conflict)
            {
                var unload = Release(loaded);
                loaded = null!;
                await unload.ConfigureAwait(false);
                return new DeployOutcome(DeployStatus.Refused, build.Hash, build.Diagnostics, Empty(), $"alias '{conflict.Alias}' is owned by source '{conflict.Owner}'");
            }

            var brainInstalls = installs(brain);
            var changed = new List<string>();
            foreach (var alias in aliases)
            {
                if (entry is not null && entry.Live.Catalog.States.ContainsKey(alias) && catalog.Current.States.ContainsKey(alias)
                    && await brainInstalls.Describe(alias, ct).ConfigureAwait(false) is not null
                    && !string.Equals(loaded.Catalog.NeedsOf(alias).Hash(), catalog.Current.NeedsOf(alias).Hash(), StringComparison.Ordinal))
                {
                    changed.Add(alias);
                }
            }

            var pending = new Pending(loaded, source, build, Bindings(aliases, bindingsPerAlias));
            return changed.Count == 0
                ? await GoLive(brain, entry, pending, build.Diagnostics, ct).ConfigureAwait(false)
                : await StageUpdate(brain, entry!, pending, changed, build.Diagnostics, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DeployOutcome> Retire(BrainId brain, string sourceKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!entries.TryGetValue((brain, sourceKey), out var entry))
            {
                return new DeployOutcome(DeployStatus.Refused, null, [], Empty(), $"source '{sourceKey}' is not deployed");
            }

            if (entry.Staged is not null)
            {
                await DropStaged(entry).ConfigureAwait(false);
            }

            var hash = entry.Live.Hash;
            var aliases = AliasesOf(entry.Live);
            var brainInstalls = installs(brain);
            foreach (var alias in aliases)
            {
                await brainInstalls.Retire(alias, ct).ConfigureAwait(false);
            }

            await registrar.Deactivate(brain, sourceKey, aliases, ct).ConfigureAwait(false);
            await store.Remove(brain, sourceKey, ct).ConfigureAwait(false);
            entries.Remove((brain, sourceKey));
            await ReleaseLive(entry).ConfigureAwait(false);
            return new DeployOutcome(DeployStatus.Retired, hash, [], Empty(), null);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeployedSource>> List(BrainId brain, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return
            [
                .. entries.Where(e => e.Key.Brain == brain)
                    .OrderBy(e => e.Key.SourceKey, StringComparer.Ordinal)
                    .Select(e => new DeployedSource(
                        e.Key.SourceKey,
                        e.Value.Live.Hash,
                        AliasesOf(e.Value.Live),
                        e.Value.Staged is null ? DeployStatus.Deployed : DeployStatus.Staged,
                        e.Value.Staged?.Loaded.Hash,
                        e.Value.At)),
            ];
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stops listening for install resolutions; deployed sources stay loaded.</summary>
    /// <returns>A task that completes when the listeners are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var registration in registrations)
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }

        registrations.Clear();
        gate.Dispose();
    }

    private static Dictionary<string, InstallOutcome> Empty() => new(StringComparer.Ordinal);

    private static List<string> AliasesOf(LoadedSource source) => [.. source.Catalog.States.Keys.Order(StringComparer.Ordinal)];

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Bindings(List<string> aliases, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> given) =>
        aliases.ToDictionary(alias => alias, alias => given.TryGetValue(alias, out var bindings) ? bindings : NoBindings, StringComparer.Ordinal);

    private (string Alias, string Owner)? Owner(BrainId brain, string sourceKey, List<string> aliases)
    {
        foreach (var ((owner, key), entry) in entries)
        {
            if (owner != brain || string.Equals(key, sourceKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (aliases.FirstOrDefault(a => entry.Live.Catalog.States.ContainsKey(a) || entry.Staged?.Loaded.Catalog.States.ContainsKey(a) == true) is { } taken)
            {
                return (taken, key);
            }
        }

        return null;
    }

    // Every alias unchanged or new: the new version goes live now, then each alias is installed (new ones may wait for the person).
    private async Task<DeployOutcome> GoLive(BrainId brain, Entry? entry, Pending next, IReadOnlyList<SynapseDiagnostic> diagnostics, CancellationToken ct)
    {
        var hash = next.Loaded.Hash;
        await registrar.Activate(brain, next.Loaded, ct).ConfigureAwait(false);
        var outcomes = await InstallAll(brain, next, ct).ConfigureAwait(false);
        if (entry is null)
        {
            entries[(brain, next.Source.SourceKey)] = new Entry(next.Loaded, DateTimeOffset.UtcNow);
            await Store(brain, next, ct).ConfigureAwait(false);
        }
        else
        {
            await Swap(brain, entry, next, ct).ConfigureAwait(false);
        }

        return new DeployOutcome(DeployStatus.Deployed, hash, diagnostics, outcomes, null);
    }

    // An installed alias's needs changed: the new catalog is staged for needs only and every alias is installed again; the changed ones wait.
    private async Task<DeployOutcome> StageUpdate(BrainId brain, Entry entry, Pending next, List<string> changed, IReadOnlyList<SynapseDiagnostic> diagnostics, CancellationToken ct)
    {
        var hash = next.Loaded.Hash;
        catalog.Stage(next.Source.SourceKey, next.Loaded.Catalog);
        entry.Staged = next;
        var outcomes = await InstallAll(brain, next, ct).ConfigureAwait(false);
        if (changed.FirstOrDefault(a => outcomes[a].Status is InstallStatus.Refused or InstallStatus.Denied) is { } refused)
        {
            await DropStaged(entry).ConfigureAwait(false);
            return new DeployOutcome(DeployStatus.Refused, hash, diagnostics, outcomes, $"the install of '{refused}' was {outcomes[refused].Status.ToString().ToLowerInvariant()}: {outcomes[refused].Reason}");
        }

        foreach (var alias in outcomes.Where(o => o.Value.Status == InstallStatus.Pending).Select(o => o.Key))
        {
            next.Waiting.Add(alias);
        }

        if (next.Waiting.Count == 0)
        {
            await CompleteStaged(brain, entry, ct).ConfigureAwait(false);
            return new DeployOutcome(DeployStatus.Deployed, hash, diagnostics, outcomes, null);
        }

        return new DeployOutcome(DeployStatus.Staged, hash, diagnostics, outcomes, null);
    }

    private async Task<Dictionary<string, InstallOutcome>> InstallAll(BrainId brain, Pending next, CancellationToken ct)
    {
        var brainInstalls = installs(brain);
        var outcomes = Empty();
        foreach (var (alias, bindings) in next.Bindings)
        {
            outcomes[alias] = await brainInstalls.Install(new InstallRequest(alias, bindings), ct).ConfigureAwait(false);
        }

        return outcomes;
    }

    // The staged version was approved: it goes live, the aliases are installed again so their hooks see the new types, the old one leaves.
    private async Task CompleteStaged(BrainId brain, Entry entry, CancellationToken ct)
    {
        var next = entry.Staged!;
        entry.Staged = null;
        await registrar.Activate(brain, next.Loaded, ct).ConfigureAwait(false);
        catalog.Unstage(next.Source.SourceKey);
        _ = await InstallAll(brain, next, ct).ConfigureAwait(false);
        await Swap(brain, entry, next, ct).ConfigureAwait(false);
    }

    // The new version is already active: aliases it no longer declares are retired and removed, it is stored, and the old context unloads.
    private async Task Swap(BrainId brain, Entry entry, Pending next, CancellationToken ct)
    {
        var dropped = AliasesOf(entry.Live).Except(next.Loaded.Catalog.States.Keys, StringComparer.Ordinal).ToList();
        if (dropped.Count > 0)
        {
            var brainInstalls = installs(brain);
            foreach (var alias in dropped)
            {
                await brainInstalls.Retire(alias, ct).ConfigureAwait(false);
            }

            await registrar.Deactivate(brain, next.Source.SourceKey, dropped, ct).ConfigureAwait(false);
        }

        await Store(brain, next, ct).ConfigureAwait(false);
        await ReleaseLive(entry, next.Loaded).ConfigureAwait(false);
        entry.At = DateTimeOffset.UtcNow;
    }

    private Task Store(BrainId brain, Pending next, CancellationToken ct) =>
        store.Put(
            brain,
            new SourceRecord(next.Source.SourceKey, next.Source.Files, next.Source.Icon, next.Build.Hash, next.Build.AssemblyName, next.Build.Assembly, next.Build.Symbols, AliasesOf(next.Loaded), DateTimeOffset.UtcNow),
            ct);

    private async Task DropStaged(Entry entry)
    {
        var key = entry.Live.SourceKey;
        catalog.Unstage(key);
        await ReleaseStaged(entry).ConfigureAwait(false);
    }

    // The release helpers take the record out of the entry in their own frame, so no async frame of the host names it while the loader collects.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task ReleaseLive(Entry entry, LoadedSource? replacement = null)
    {
        var old = entry.Live;
        entry.Live = replacement!;
        return Release(old);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task ReleaseStaged(Entry entry)
    {
        var staged = entry.Staged!.Loaded;
        entry.Staged = null;
        return Release(staged);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task Release(LoadedSource loaded) => Collected(loaded.SourceKey, loaded.Hash, loader.Unload(loaded));

    private async Task Collected(string sourceKey, string hash, Task<bool> unload)
    {
        if (!await unload.ConfigureAwait(false))
        {
            LogLeaked(sourceKey, hash);
            lock (leaked) { leaked.Add((sourceKey, hash)); }
        }
    }

    private async Task Listen()
    {
        if (Volatile.Read(ref listening))
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (listening)
            {
                return;
            }

            registrations.Add(await listeners.Listen<Installed>(ListenerName, (publisher, signal, ct) => OnResolved(publisher.Brain, signal.Alias, approved: true, ct)).ConfigureAwait(false));
            registrations.Add(await listeners.Listen<InstallDenied>(ListenerName, (publisher, signal, ct) => OnResolved(publisher.Brain, signal.Alias, approved: false, ct)).ConfigureAwait(false));
            Volatile.Write(ref listening, true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task OnResolved(BrainId brain, string alias, bool approved, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entry = entries.FirstOrDefault(e => e.Key.Brain == brain && e.Value.Staged?.Waiting.Contains(alias) == true).Value;
            if (entry is null)
            {
                return;
            }

            if (!approved)
            {
                await DropStaged(entry).ConfigureAwait(false);
                return;
            }

            entry.Staged!.Waiting.Remove(alias);
            if (entry.Staged.Waiting.Count == 0)
            {
                await CompleteStaged(brain, entry, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The context of synapse source {SourceKey} ({Hash}) did not unload.")]
    private partial void LogLeaked(string sourceKey, string hash);

    private sealed class Entry(LoadedSource live, DateTimeOffset at)
    {
        public LoadedSource Live { get; set; } = live;

        public Pending? Staged { get; set; }

        public DateTimeOffset At { get; set; } = at;
    }

    private sealed record Pending(LoadedSource Loaded, SynapseSource Source, SynapseBuild Build, Dictionary<string, IReadOnlyDictionary<string, string>> Bindings)
    {
        public HashSet<string> Waiting { get; } = new(StringComparer.Ordinal);
    }
}
