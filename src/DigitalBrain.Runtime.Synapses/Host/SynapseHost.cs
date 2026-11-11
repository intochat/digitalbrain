using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Kernel;
using DigitalBrain.Units;
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

        // The boundary: every reference in the emitted bytes is on the allow-list, checked before anything loads or any staged
        // build is touched. DB0045 in the compiler is advisory; a build that skipped it is still refused here, and a refused
        // deploy leaves the live and staged versions as they were.
        if (AllowedReferences.FirstDisallowed(build.Assembly) is { } disallowed)
        {
            var reason = $"'{disallowed}' is outside the surface a synapse assembly may reference";
            return new DeployOutcome(DeployStatus.Refused, build.Hash, [.. build.Diagnostics, new SynapseDiagnostic(source.SourceKey, "DB0045", 0, reason)], Empty(), reason);
        }

        await Listen().ConfigureAwait(false);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            entries.TryGetValue((brain, source.SourceKey), out var entry);
            var effective = Effective(source, bindingsPerAlias);
            if (entry is not null && string.Equals(entry.Live.Hash, build.Hash, StringComparison.Ordinal))
            {
                return await RebindOrKeep(brain, entry, source, build, effective, ct).ConfigureAwait(false);
            }

            if (entry is not null)
            {
                entry.Rebind = null;
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

            var pending = new Pending(loaded!, source, build, Bindings(aliases, effective));
            if (changed.Count > 0)
            {
                return await StageUpdate(brain, entry!, pending, changed, build.Diagnostics, ct).ConfigureAwait(false);
            }

            try
            {
                return await GoLive(brain, entry, pending, build.Diagnostics, ct).ConfigureAwait(false);
            }
            catch when (pending.Abandoned)
            {
                // This frame is the last to name the record: it drops it before the load unloads.
                var release = Release(brain, pending.Loaded, staged: false);
                loaded = null;
                pending = null!;
                await release.ConfigureAwait(false);
                throw;
            }
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

            if (!SourceUsedElsewhere(brain, sourceKey))
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
            var live = entries.Where(e => e.Key.Brain == brain)
                .Select(e => new DeployedSource(
                    e.Key.SourceKey,
                    e.Value.Live.Hash,
                    AliasesOf(e.Value.Live),
                    e.Value.Staged is null ? DeployStatus.Deployed : DeployStatus.Staged,
                    e.Value.Staged?.Loaded.Hash,
                    e.Value.At))
                .ToList();

            // A stored version the restorer refuses (outside the current boundary, or slots without bindings) is listed so the person
            // sees it needs a rebuild instead of it silently missing.
            foreach (var record in await store.List(brain, ct).ConfigureAwait(false))
            {
                if (!live.Any(s => string.Equals(s.SourceKey, record.SourceKey, StringComparison.Ordinal)) && SynapseRestorer.RebuildReason(record) is { } reason)
                {
                    live.Add(new DeployedSource(record.SourceKey, record.Hash, record.Aliases, DeployStatus.RebuildRequired, null, record.DeployedAt, reason));
                }
            }

            return [.. live.OrderBy(s => s.SourceKey, StringComparer.Ordinal)];
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

        if (!SourceUsedElsewhere(brain, record.SourceKey))
        {
            await registrar.Deactivate(brain, sourceKey, record.Aliases, ct).ConfigureAwait(false);
        }

        await store.Remove(brain, sourceKey, ct).ConfigureAwait(false);
        return new DeployOutcome(DeployStatus.Retired, record.Hash, [], Empty(), null);
    }

    private static Dictionary<string, InstallOutcome> Empty() => new(StringComparer.Ordinal);

    private static List<string> AliasesOf(LoadedSource source) => [.. source.Catalog.States.Keys.Order(StringComparer.Ordinal)];

    // The bindings a deploy installs with: a non-empty argument wins, otherwise the source's own.
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Effective(SynapseSource source, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> argument) =>
        argument.Count > 0 ? argument : source.Bindings ?? argument;

    private static bool SameBindings(List<string> aliases, Dictionary<string, IReadOnlyDictionary<string, string>> left, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> right) =>
        aliases.All(alias =>
        {
            var l = left.TryGetValue(alias, out var x) ? x : NoBindings;
            var r = right.TryGetValue(alias, out var y) ? y : NoBindings;
            return l.Count == r.Count && l.All(pair => r.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
        });

    // The live build was deployed again. An alias the deploy gives no bindings keeps the stored ones; with the bindings it is stored with
    // the deploy is unchanged. Others are checked for every alias first (a refusal touches nothing), then the aliases are installed again
    // (the person already approved these needs) and the record keeps the new bindings, unless an install still waits for the person.
    private async Task<DeployOutcome> RebindOrKeep(BrainId brain, Entry entry, SynapseSource source, SynapseBuild build, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> effective, CancellationToken ct)
    {
        var aliases = AliasesOf(entry.Live);
        var stored = (await store.Get(brain, source.SourceKey, ct).ConfigureAwait(false))?.Bindings ?? new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var wanted = aliases.ToDictionary(a => a, a => effective.TryGetValue(a, out var given) ? given : stored.TryGetValue(a, out var kept) ? kept : NoBindings, StringComparer.Ordinal);
        if (SameBindings(aliases, wanted, stored))
        {
            return new DeployOutcome(DeployStatus.Unchanged, build.Hash, build.Diagnostics, Empty(), null);
        }

        var brainInstalls = installs(brain);
        foreach (var alias in aliases)
        {
            if (await brainInstalls.Validate(new InstallRequest(alias, wanted[alias]), ct).ConfigureAwait(false) is { } problem)
            {
                return new DeployOutcome(DeployStatus.Refused, build.Hash, build.Diagnostics, Empty(), $"the bindings of '{alias}' are refused: {problem}");
            }
        }

        var next = new Pending(entry.Live, source, build, wanted);
        var outcomes = await InstallAll(brain, next, ct).ConfigureAwait(false);
        if (aliases.FirstOrDefault(a => outcomes[a].Status is InstallStatus.Refused or InstallStatus.Denied) is { } refused)
        {
            return new DeployOutcome(DeployStatus.Refused, build.Hash, build.Diagnostics, outcomes, $"the install of '{refused}' was {outcomes[refused].Status.ToString().ToLowerInvariant()}: {outcomes[refused].Reason}");
        }

        // An alias still waiting for the person keeps its stored bindings until the ask is approved (ResolveRebind stores them then).
        var waiting = aliases.Where(a => outcomes[a].Status == InstallStatus.Pending).ToList();
        entry.Rebind = waiting.Count == 0 ? null : new Rebind(
            waiting.ToDictionary(a => a, a => outcomes[a].Reason ?? "", StringComparer.Ordinal),
            waiting.ToDictionary(a => a, a => wanted[a], StringComparer.Ordinal));
        if (waiting.Count < aliases.Count)
        {
            var kept = aliases.ToDictionary(a => a, a => waiting.Contains(a) ? stored.TryGetValue(a, out var old) ? old : NoBindings : wanted[a], StringComparer.Ordinal);
            await Store(brain, next with { Bindings = kept }, ct).ConfigureAwait(false);
        }

        return new DeployOutcome(DeployStatus.Deployed, build.Hash, build.Diagnostics, outcomes, null);
    }

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
        var key = next.Source.SourceKey;
        var aliases = AliasesOf(next.Loaded);
        Dictionary<string, InstallOutcome> outcomes;
        try
        {
            await ActivateOnce(brain, next.Loaded, ct).ConfigureAwait(false);
            _ = Reference(brain, next.Loaded, staged: false);
            outcomes = await InstallAll(brain, next, ct).ConfigureAwait(false);
        }
        catch
        {
            // Nothing went live: the activation is undone here; Deploy then releases the load once its own frame no longer names the record.
            next.Abandoned = true;
            await UndoActivation(brain, entry, key, hash, aliases).ConfigureAwait(false);
            throw;
        }

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

    // A deploy that failed after its activation: when this brain is the only holder of the new load, a first deploy deactivates its aliases
    // and an update makes the old live version active again (the registrar replaces by alias). Another brain's hold keeps it all as it is.
    private async Task UndoActivation(BrainId brain, Entry? entry, string sourceKey, string hash, List<string> aliases)
    {
        var alone = !loads.TryGetValue((sourceKey, hash), out var shared) || shared.Brains.All(b => b.Brain == brain);
        if (!alone || SourceUsedElsewhere(brain, sourceKey))
        {
            return;
        }

        if (entry is null)
        {
            await registrar.Deactivate(brain, sourceKey, aliases, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await registrar.Activate(brain, entry.Live, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Undoes the activation of a source whose adoption failed, so its aliases do not stay registered over an unloaded context: they are
    /// deactivated unless another brain still holds the source. A disposed host keeps its in-memory loads, so the guard still applies to it.
    /// </summary>
    /// <param name="brain">The brain.</param>
    /// <param name="sourceKey">The source key.</param>
    /// <param name="aliases">The aliases the source made live.</param>
    /// <returns>A task that completes when the aliases are deactivated or kept.</returns>
    internal async Task Unwind(BrainId brain, string sourceKey, IReadOnlyList<string> aliases)
    {
        var held = false;
        try
        {
            await gate.WaitAsync().ConfigureAwait(false);
            held = true;
        }
        catch (ObjectDisposedException)
        {
            // The gate is gone with the host; its dictionaries are not, and nothing runs on a disposed host to change them.
        }

        try
        {
            if (!SourceUsedElsewhere(brain, sourceKey))
            {
                await registrar.Deactivate(brain, sourceKey, aliases, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            if (held)
            {
                gate.Release();
            }
        }
    }

    // An installed alias's needs changed: the new catalog is staged for needs only and every alias is installed again; the changed ones wait.
    private async Task<DeployOutcome> StageUpdate(BrainId brain, Entry entry, Pending next, List<string> changed, IReadOnlyList<SynapseDiagnostic> diagnostics, CancellationToken ct)
    {
        var hash = next.Loaded.Hash;
        catalog.Stage(next.Source.SourceKey, next.Loaded.Catalog);
        entry.Staged = next;
        _ = Reference(brain, next.Loaded, staged: true);
        Dictionary<string, InstallOutcome> outcomes;
        try
        {
            outcomes = await InstallAll(brain, next, ct).ConfigureAwait(false);
        }
        catch
        {
            catalog.Unstage(next.Source.SourceKey);
            next = null!;
            await ReleaseStaged(brain, entry).ConfigureAwait(false);
            throw;
        }

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

            if (!SourceUsedElsewhere(brain, next.Source.SourceKey))
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
            new SourceRecord(next.Source.SourceKey, next.Source.Files, next.Source.Icon, next.Build.Hash, next.Build.AssemblyName, next.Build.Assembly, next.Build.Symbols, AliasesOf(next.Loaded), DateTimeOffset.UtcNow, next.Bindings),
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

    // The registrar deactivates by alias, process-wide: while another brain references any load of the source key, live or staged,
    // its aliases stay.
    private bool SourceUsedElsewhere(BrainId brain, string sourceKey) =>
        loads.Any(load => string.Equals(load.Key.SourceKey, sourceKey, StringComparison.Ordinal) && load.Value.Brains.Any(b => b.Brain != brain));

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
            await ResolveRebind(brain, signal).ConfigureAwait(false);
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

    // The ask of a rebind was answered: an approval stores the bindings the ask carried, a denial drops them.
    private async Task ResolveRebind(BrainId brain, Signal signal)
    {
        foreach (var ((owner, key), entry) in entries)
        {
            if (owner != brain || entry.Rebind is not { } rebind)
            {
                continue;
            }

            var alias = signal switch
            {
                Installed installed when rebind.Waiting.ContainsKey(installed.Alias) => installed.Alias,
                InstallDenied denied when rebind.Waiting.TryGetValue(denied.Alias, out var plan) && string.Equals(plan, denied.PlanId, StringComparison.Ordinal) => denied.Alias,
                _ => null,
            };
            if (alias is null)
            {
                continue;
            }

            // The pending rebind is dropped only once the approved bindings are stored: a failed Put keeps it, so the next resolution
            // (or a retry) can still store them.
            var bindings = rebind.Bindings[alias];
            if (signal is Installed && await store.Get(brain, key, CancellationToken.None).ConfigureAwait(false) is { } record)
            {
                var merged = new Dictionary<string, IReadOnlyDictionary<string, string>>(record.Bindings ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(), StringComparer.Ordinal)
                {
                    [alias] = bindings,
                };
                await store.Put(brain, record with { Bindings = merged }, CancellationToken.None).ConfigureAwait(false);
            }

            _ = rebind.Waiting.Remove(alias);
            _ = rebind.Bindings.Remove(alias);
            if (rebind.Waiting.Count == 0)
            {
                entry.Rebind = null;
            }

            return;
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

        // The asks of a rebind of the live build still waiting for the person.
        public Rebind? Rebind { get; set; }

        public DateTimeOffset At { get; set; } = at;
    }

    // Alias to the plan id of its ask, and alias to the bindings the ask carries.
    private sealed record Rebind(Dictionary<string, string> Waiting, Dictionary<string, IReadOnlyDictionary<string, string>> Bindings);

    private sealed record Pending(LoadedSource Loaded, SynapseSource Source, SynapseBuild Build, Dictionary<string, IReadOnlyDictionary<string, string>> Bindings)
    {
        // Alias to the plan id of its ask.
        public Dictionary<string, string> Waiting { get; } = new(StringComparer.Ordinal);

        // Its go-live failed before anything was committed: the load is to be released.
        public bool Abandoned { get; set; }
    }
}
