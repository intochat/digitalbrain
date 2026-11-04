using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Spi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// Deploys, updates and retires generated synapse sources: compiles, loads into a collectible context, makes the source live through the
/// registrar and installs its aliases. A version that changes the needs of an installed alias is staged (its catalog serves
/// <see cref="ISynapseCatalogSource.ForNeeds"/> only) until every changed alias is <see cref="Installed"/>; an <see cref="InstallDenied"/>
/// drops it and the old version stays live. Resolutions are queued and applied one at a time by a background loop, so a listener never waits
/// on a deploy in progress. Per brain and source key it keeps the live and the staged <see cref="LoadedSource"/> in memory.
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

    // One live load per source key and hash, shared by every brain (the registrar is process-wide): who references it, live or staged.
    private readonly Dictionary<(string SourceKey, string Hash), Shared> loads = [];
    private readonly List<IAsyncDisposable> registrations = [];
    private readonly Channel<(BrainId Brain, Signal Signal, TaskCompletionSource Done)> resolutions =
        Channel.CreateUnbounded<(BrainId Brain, Signal Signal, TaskCompletionSource Done)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Action<Task>? track;
    private Task drain = Task.CompletedTask;
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
    /// <param name="track">Given the completion of each queued install resolution, as a test host tracks outstanding work; optional.</param>
    public SynapseHost(
        SynapseCompiler compiler,
        SynapseLoader loader,
        ISynapseRegistrar registrar,
        ISynapseCatalogSource catalog,
        Func<BrainId, IInstalls> installs,
        ISourceStore store,
        IClusterListeners listeners,
        ILogger<SynapseHost>? logger = null,
        Action<Task>? track = null)
    {
        this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.installs = installs ?? throw new ArgumentNullException(nameof(installs));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.listeners = listeners ?? throw new ArgumentNullException(nameof(listeners));
        this.logger = logger ?? NullLogger<SynapseHost>.Instance;
        this.track = track;
    }

    /// <summary>The compiler the host builds with (facts read its emit count).</summary>
    internal SynapseCompiler Compiler => compiler;

    /// <summary>The source key and hash of every context an unload or retire could not collect.</summary>
    public IReadOnlyList<(string SourceKey, string Hash)> Leaked
    {
        get
        {
            lock (leaked) { return [.. leaked]; }
        }
    }

    // How many builds the compiler has emitted.
    internal int Compiles => compiler.Emits;

    /// <summary>
    /// Takes <paramref name="loaded"/>, restored from <paramref name="record"/> and already made live, as the live version of its source,
    /// so it can be updated and retired; a source the host already holds is left as it is.
    /// </summary>
    /// <param name="brain">The brain.</param>
    /// <param name="loaded">The restored source.</param>
    /// <param name="record">The record it was restored from.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>True when the host took it.</returns>
    public async Task<bool> Adopt(BrainId brain, LoadedSource loaded, SourceRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(record);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!entries.TryAdd((brain, loaded.SourceKey), new Entry(loaded, record.DeployedAt)))
            {
                return false;
            }

            Reference(brain, loaded, staged: false).Activated = true;
            return true;
        }
        finally
        {
            gate.Release();
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

            if (string.Equals(entry?.Staged?.Loaded.Hash, build.Hash, StringComparison.Ordinal))
            {
                return new DeployOutcome(DeployStatus.Staged, build.Hash, build.Diagnostics, Empty(), "this version is already staged");
            }

            if (entry?.Staged is not null)
            {
                await DropStaged(brain, entry, ct).ConfigureAwait(false);
            }

            var refusal = LoadChecked(brain, source.SourceKey, build, out var loaded, out var unload);
            if (refusal is not null)
            {
                await unload.ConfigureAwait(false);
                return new DeployOutcome(refusal.Value.Status, build.Hash, build.Diagnostics, Empty(), refusal.Value.Reason);
            }

            var aliases = AliasesOf(loaded!);
            var brainInstalls = installs(brain);
            var changed = new List<string>();
            foreach (var alias in aliases)
            {
                if (entry is not null && entry.Live.Catalog.States.ContainsKey(alias) && catalog.Current.States.ContainsKey(alias)
                    && await brainInstalls.Describe(alias, ct).ConfigureAwait(false) is not null
                    && !string.Equals(loaded!.Catalog.NeedsOf(alias).Hash(), catalog.Current.NeedsOf(alias).Hash(), StringComparison.Ordinal))
                {
                    changed.Add(alias);
                }
            }

            var pending = new Pending(loaded!, source, build, Bindings(aliases, bindingsPerAlias));
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
                return await RetireStored(brain, sourceKey, ct).ConfigureAwait(false);
            }

            if (entry.Staged is not null)
            {
                await DropStaged(brain, entry, ct).ConfigureAwait(false);
            }

            var hash = entry.Live.Hash;
            var aliases = AliasesOf(entry.Live);
            var brainInstalls = installs(brain);
            foreach (var alias in aliases)
            {
                await brainInstalls.Retire(alias, ct).ConfigureAwait(false);
            }

            if (!LiveElsewhere(brain, entry.Live))
            {
                await registrar.Deactivate(brain, sourceKey, aliases, ct).ConfigureAwait(false);
            }

            await store.Remove(brain, sourceKey, ct).ConfigureAwait(false);
            entries.Remove((brain, sourceKey));
            await ReleaseLive(brain, entry).ConfigureAwait(false);
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

    /// <summary>Stops listening for install resolutions and applies the queued ones; deployed sources stay loaded.</summary>
    /// <returns>A task that completes when the listeners are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        resolutions.Writer.TryComplete();
        await drain.ConfigureAwait(false);
        foreach (var registration in registrations)
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }

        registrations.Clear();
        gate.Dispose();
    }

    // A source stored but not loaded here (not yet restored after a restart): its aliases are retired and its record removed.
    private async Task<DeployOutcome> RetireStored(BrainId brain, string sourceKey, CancellationToken ct)
    {
        if (await store.Get(brain, sourceKey, ct).ConfigureAwait(false) is not { } record)
        {
            return new DeployOutcome(DeployStatus.Refused, null, [], Empty(), $"source '{sourceKey}' is not deployed");
        }

        var brainInstalls = installs(brain);
        foreach (var alias in record.Aliases)
        {
            await brainInstalls.Retire(alias, ct).ConfigureAwait(false);
        }

        await registrar.Deactivate(brain, sourceKey, record.Aliases, ct).ConfigureAwait(false);
        await store.Remove(brain, sourceKey, ct).ConfigureAwait(false);
        return new DeployOutcome(DeployStatus.Retired, record.Hash, [], Empty(), null);
    }

    private static Dictionary<string, InstallOutcome> Empty() => new(StringComparer.Ordinal);

    private static List<string> AliasesOf(LoadedSource source) => [.. source.Catalog.States.Keys.Order(StringComparer.Ordinal)];

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Bindings(List<string> aliases, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> given) =>
        aliases.ToDictionary(alias => alias, alias => given.TryGetValue(alias, out var bindings) ? bindings : NoBindings, StringComparer.Ordinal);

    // Loads the build and checks its aliases in a frame of its own: a refused load is released here, so no async frame of the host names
    // the record while the loader collects it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private (DeployStatus Status, string Reason)? LoadChecked(BrainId brain, string sourceKey, SynapseBuild build, out LoadedSource? loaded, out Task unload)
    {
        unload = Task.CompletedTask;
        try
        {
            loaded = loads.TryGetValue((sourceKey, build.Hash), out var shared) ? shared.Loaded : loader.Load(build);
        }
        catch (Exception error) when (error is InvalidOperationException or BadImageFormatException or System.Reflection.ReflectionTypeLoadException or SynapseShapeException)
        {
            loaded = null;
            return (DeployStatus.Failed, $"the build does not load: {error.Message}");
        }

        if (Owner(brain, sourceKey, AliasesOf(loaded)) is { } conflict)
        {
            unload = loads.ContainsKey((sourceKey, build.Hash)) ? Task.CompletedTask : Unload(loaded);
            loaded = null;
            return (DeployStatus.Refused, $"alias '{conflict.Alias}' is owned by source '{conflict.Owner}'");
        }

        return null;
    }

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
        await ActivateOnce(brain, next.Loaded, ct).ConfigureAwait(false);
        _ = Reference(brain, next.Loaded, staged: false);
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
        _ = Reference(brain, next.Loaded, staged: true);
        var outcomes = await InstallAll(brain, next, ct).ConfigureAwait(false);
        if (changed.FirstOrDefault(a => outcomes[a].Status is InstallStatus.Refused or InstallStatus.Denied) is { } refused)
        {
            await DropStaged(brain, entry, ct).ConfigureAwait(false);
            return new DeployOutcome(DeployStatus.Refused, hash, diagnostics, outcomes, $"the install of '{refused}' was {outcomes[refused].Status.ToString().ToLowerInvariant()}: {outcomes[refused].Reason}");
        }

        foreach (var (alias, outcome) in outcomes.Where(o => o.Value.Status == InstallStatus.Pending))
        {
            next.Waiting[alias] = outcome.Reason ?? "";
        }

        if (next.Waiting.Count == 0)
        {
            await CompleteStaged(brain, entry, ct).ConfigureAwait(false);
            return new DeployOutcome(DeployStatus.Deployed, hash, diagnostics, outcomes, null);
        }

        await store.Stage(brain, next.Source.SourceKey, hash, ct).ConfigureAwait(false);
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
        await ActivateOnce(brain, next.Loaded, ct).ConfigureAwait(false);
        _ = Reference(brain, next.Loaded, staged: false);
        Dereference(brain, next.Loaded, staged: true);
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

            if (!LiveElsewhere(brain, entry.Live))
            {
                await registrar.Deactivate(brain, next.Source.SourceKey, dropped, ct).ConfigureAwait(false);
            }
        }

        await Store(brain, next, ct).ConfigureAwait(false);
        await ReleaseLive(brain, entry, next.Loaded).ConfigureAwait(false);
        entry.At = DateTimeOffset.UtcNow;
    }

    private Task Store(BrainId brain, Pending next, CancellationToken ct) =>
        store.Put(
            brain,
            new SourceRecord(next.Source.SourceKey, next.Source.Files, next.Source.Icon, next.Build.Hash, next.Build.AssemblyName, next.Build.Assembly, next.Build.Symbols, AliasesOf(next.Loaded), DateTimeOffset.UtcNow),
            ct);

    // The stage leaves: its asks still waiting are cancelled (superseded), its catalog unstaged and its context unloaded.
    private async Task DropStaged(BrainId brain, Entry entry, CancellationToken ct)
    {
        var key = entry.Live.SourceKey;
        var asked = entry.Staged!.Waiting.Keys.ToList();
        entry.Staged.Waiting.Clear();
        var brainInstalls = installs(brain);
        foreach (var alias in asked)
        {
            await brainInstalls.CancelPending(alias, ct).ConfigureAwait(false);
        }

        catalog.Unstage(key);
        await ReleaseStaged(brain, entry).ConfigureAwait(false);
    }

    // The release helpers take the record out of the entry in their own frame, so no async frame of the host names it while the loader collects.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task ReleaseLive(BrainId brain, Entry entry, LoadedSource? replacement = null)
    {
        var old = entry.Live;
        entry.Live = replacement!;
        return Release(brain, old, staged: false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task ReleaseStaged(BrainId brain, Entry entry)
    {
        var staged = entry.Staged!.Loaded;
        entry.Staged = null;
        return Release(brain, staged, staged: true);
    }

    // Drops the brain's reference; the shared load unloads only when no brain references it, live or staged.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task Release(BrainId brain, LoadedSource loaded, bool staged)
    {
        Dereference(brain, loaded, staged);
        return loads.ContainsKey((loaded.SourceKey, loaded.Hash)) ? Task.CompletedTask : Unload(loaded);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task Unload(LoadedSource loaded) => Collected(loaded.SourceKey, loaded.Hash, loader.Unload(loaded));

    private Shared Reference(BrainId brain, LoadedSource loaded, bool staged)
    {
        if (!loads.TryGetValue((loaded.SourceKey, loaded.Hash), out var shared))
        {
            loads[(loaded.SourceKey, loaded.Hash)] = shared = new Shared(loaded);
        }

        shared.Brains.Add((brain, staged));
        return shared;
    }

    private void Dereference(BrainId brain, LoadedSource loaded, bool staged)
    {
        if (loads.TryGetValue((loaded.SourceKey, loaded.Hash), out var shared) && shared.Brains.Remove((brain, staged)) && shared.Brains.Count == 0)
        {
            loads.Remove((loaded.SourceKey, loaded.Hash));
        }
    }

    private bool LiveElsewhere(BrainId brain, LoadedSource loaded) =>
        loads.TryGetValue((loaded.SourceKey, loaded.Hash), out var shared) && shared.Brains.Any(b => !b.Staged && b.Brain != brain);

    // The registrar is process-wide: a load another brain already made live is not activated again.
    private async Task ActivateOnce(BrainId brain, LoadedSource loaded, CancellationToken ct)
    {
        if (loads.TryGetValue((loaded.SourceKey, loaded.Hash), out var shared) && shared.Activated)
        {
            return;
        }

        await registrar.Activate(brain, loaded, ct).ConfigureAwait(false);
        Reference(brain, loaded, staged: false).Activated = true;
    }

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

            drain = Task.Run(Drain);
            registrations.Add(await listeners.Listen<Installed>(ListenerName, (publisher, signal, _) => Enqueue(publisher.Brain, signal)).ConfigureAwait(false));
            registrations.Add(await listeners.Listen<InstallDenied>(ListenerName, (publisher, signal, _) => Enqueue(publisher.Brain, signal)).ConfigureAwait(false));
            Volatile.Write(ref listening, true);
        }
        finally
        {
            gate.Release();
        }
    }

    private Task Enqueue(BrainId brain, Signal signal)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (resolutions.Writer.TryWrite((brain, signal, done)))
        {
            track?.Invoke(done.Task);
        }

        return Task.CompletedTask;
    }

    private async Task Drain()
    {
        await foreach (var (brain, signal, done) in resolutions.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await OnResolved(brain, signal).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                LogResolutionFailed(error);
            }

            done.TrySetResult();
        }
    }

    private async Task OnResolved(BrainId brain, Signal signal)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entry = entries.FirstOrDefault(e => e.Key.Brain == brain && e.Value.Staged is { } staged && Awaits(staged, signal)).Value;
            if (entry is null)
            {
                return;
            }

            if (signal is InstallDenied denied)
            {
                entry.Staged!.Waiting.Remove(denied.Alias);
                await DropStaged(brain, entry, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            entry.Staged!.Waiting.Remove(((Installed)signal).Alias);
            if (entry.Staged.Waiting.Count == 0)
            {
                await CompleteStaged(brain, entry, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    // An Installed with the staged needs, or an InstallDenied of the very ask the stage made, resolves the stage's ask of that alias.
    private static bool Awaits(Pending staged, Signal signal) => signal switch
    {
        Installed installed => staged.Waiting.ContainsKey(installed.Alias)
            && string.Equals(staged.Loaded.Catalog.NeedsOf(installed.Alias).Hash(), installed.NeedsHash, StringComparison.Ordinal),
        InstallDenied denied => staged.Waiting.TryGetValue(denied.Alias, out var plan) && string.Equals(plan, denied.PlanId, StringComparison.Ordinal),
        _ => false,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "An install resolution could not be applied.")]
    private partial void LogResolutionFailed(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The context of synapse source {SourceKey} ({Hash}) did not unload.")]
    private partial void LogLeaked(string sourceKey, string hash);

    private sealed class Shared(LoadedSource loaded)
    {
        public LoadedSource Loaded { get; } = loaded;

        public HashSet<(BrainId Brain, bool Staged)> Brains { get; } = [];

        public bool Activated { get; set; }
    }

    private sealed class Entry(LoadedSource live, DateTimeOffset at)
    {
        public LoadedSource Live { get; set; } = live;

        public Pending? Staged { get; set; }

        public DateTimeOffset At { get; set; } = at;
    }

    private sealed record Pending(LoadedSource Loaded, SynapseSource Source, SynapseBuild Build, Dictionary<string, IReadOnlyDictionary<string, string>> Bindings)
    {
        // Alias to the plan id of its ask.
        public Dictionary<string, string> Waiting { get; } = new(StringComparer.Ordinal);
    }
}
