using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

namespace DigitalBrain.Units;

/// <summary>
/// Durable aggregate neuron managing the deployment lifecycle of a single source key at (brain, "synapse.deployment", sourceKey).
/// </summary>
[Neuron("synapse.deployment")]
public sealed class UnitDeployment(LiveBindings? live = null) : Neuron<DeploymentState>, IUnitDeployment
{
    private string SourceKey => Id.Key;

    /// <inheritdoc />
    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        await Context.Synapses.EnsureBrainWide(typeof(Installed), typeof(InstallDenied)).ConfigureAwait(true);
        if (State.ReadyToPromote)
        {
            await Promote(State, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task<DeployOutcome> Propose(
        StoredBuild build,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(bindings);

        if (State.Live is { } liveBuild && liveBuild.Ref == build.Ref)
        {
            return await Rebind(build, bindings, ct).ConfigureAwait(true);
        }

        var sources = Context.Get<ISources>(new NeuronId(Id.Brain, "synapse.sources", "default"));
        var conflict = await sources.Claim(SourceKey, build.Aliases, ct).ConfigureAwait(true);
        if (conflict is not null)
        {
            return new DeployOutcome(
                DeployStatus.Refused,
                build.Ref.Hash,
                [],
                new Dictionary<string, InstallOutcome>(),
                $"Alias '{conflict.Alias}' is already owned by source '{conflict.Owner}'.");
        }

        if (State.Staged is not null)
        {
            await Supersede("superseded", ct).ConfigureAwait(true);
        }

        var installs = Context.Get<IInstalls>(new NeuronId(Id.Brain, "synapse.installs", "default"));
        var needsDiff = new List<string>();
        foreach (var alias in build.Aliases)
        {
            var consent = await installs.Describe(alias, ct).ConfigureAwait(true);
            if (State.Live is not null
                && consent is not null
                && build.NeedsHashes.TryGetValue(alias, out var expected)
                && !string.Equals(consent.NeedsHash, expected, StringComparison.Ordinal))
            {
                needsDiff.Add(alias);
            }
        }

        // Save-before-ask: persist staged proposal durably before asking
        var proposed = State.Propose(build, needsDiff);
        await Save(proposed, [new Staged(SourceKey, build.Ref.Hash)]).ConfigureAwait(true);

        var installOutcomes = new Dictionary<string, InstallOutcome>(StringComparer.Ordinal);
        try
        {
            foreach (var alias in build.Aliases)
            {
                var aliasBindings = bindings.TryGetValue(alias, out var b) ? b : new Dictionary<string, string>();
                var outcome = await installs.Install(new InstallRequest(alias, aliasBindings), ct).ConfigureAwait(true);
                installOutcomes[alias] = outcome;
            }
        }
        catch (Exception)
        {
            await Supersede("install failed", ct).ConfigureAwait(true);
            throw;
        }

        var refused = installOutcomes.FirstOrDefault(kvp => kvp.Value.Status == InstallStatus.Refused || kvp.Value.Status == InstallStatus.Denied);
        if (refused.Key is not null)
        {
            await Supersede(refused.Value.Reason ?? "install refused", ct).ConfigureAwait(true);
            return new DeployOutcome(DeployStatus.Refused, build.Ref.Hash, [], installOutcomes, refused.Value.Reason ?? $"Install of '{refused.Key}' refused.");
        }

        var pendingPlans = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in needsDiff)
        {
            if (installOutcomes.TryGetValue(alias, out var outcome) && outcome.Status == InstallStatus.Pending && outcome.Reason is not null)
            {
                pendingPlans[alias] = outcome.Reason;
            }
        }

        var asked = State.Asked(pendingPlans);
        if (asked.Staged is null)
        {
            var isDeployed = State.Live is { } promoted && promoted.Ref == build.Ref;
            return new DeployOutcome(isDeployed ? DeployStatus.Deployed : DeployStatus.Refused, build.Ref.Hash, [], installOutcomes, null);
        }

        if (asked.ReadyToPromote)
        {
            await Promote(asked, ct).ConfigureAwait(true);
            return new DeployOutcome(DeployStatus.Deployed, build.Ref.Hash, [], installOutcomes, null);
        }

        await Save(asked, [new StageChanged(SourceKey, build.Ref.Hash, [.. asked.Staged!.Waiting.Keys])]).ConfigureAwait(true);
        return new DeployOutcome(DeployStatus.Staged, build.Ref.Hash, [], installOutcomes, null);
    }

    private async Task<DeployOutcome> Rebind(
        StoredBuild build,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindings,
        CancellationToken ct)
    {
        var installs = Context.Get<IInstalls>(new NeuronId(Id.Brain, "synapse.installs", "default"));
        var wanted = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var changed = false;

        foreach (var alias in build.Aliases)
        {
            var consent = await installs.Describe(alias, ct).ConfigureAwait(true);
            var stored = consent?.Bindings ?? new Dictionary<string, string>(StringComparer.Ordinal);
            if (bindings.TryGetValue(alias, out var given))
            {
                if (!SameBindings(given, stored))
                {
                    wanted[alias] = given;
                    changed = true;
                }
                else
                {
                    wanted[alias] = stored;
                }
            }
            else
            {
                wanted[alias] = stored;
            }
        }

        if (!changed)
        {
            return new DeployOutcome(DeployStatus.Unchanged, build.Ref.Hash, [], new Dictionary<string, InstallOutcome>(), null);
        }

        foreach (var (alias, aliasBindings) in wanted)
        {
            var problem = await installs.Validate(new InstallRequest(alias, aliasBindings), ct).ConfigureAwait(true);
            if (problem is not null)
            {
                return new DeployOutcome(DeployStatus.Refused, build.Ref.Hash, [], new Dictionary<string, InstallOutcome>(), $"the bindings of '{alias}' are refused: {problem}");
            }
        }

        var outcomes = new Dictionary<string, InstallOutcome>(StringComparer.Ordinal);
        foreach (var (alias, aliasBindings) in wanted)
        {
            var outcome = await installs.Install(new InstallRequest(alias, aliasBindings), ct).ConfigureAwait(true);
            outcomes[alias] = outcome;
            if (outcome.Status is InstallStatus.Refused or InstallStatus.Denied)
            {
                return new DeployOutcome(DeployStatus.Refused, build.Ref.Hash, [], outcomes, outcome.Reason);
            }
        }

        var status = DeployStatus.Deployed;
        return new DeployOutcome(status, build.Ref.Hash, [], outcomes, null);
    }

    private static bool SameBindings(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var bv) || !string.Equals(v, bv, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>Reacts to an approval signal from installs.</summary>
    [On]
    public async Task OnInstalled(Installed installed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(installed);
        if (State.Staged is null) { return; }
        var next = State.Approve(installed.Alias, installed.NeedsHash);
        if (ReferenceEquals(next, State)) { return; }
        if (next.ReadyToPromote)
        {
            await Promote(next, ct).ConfigureAwait(true);
        }
        else
        {
            await Save(next, [new StageChanged(SourceKey, next.Staged!.Build.Ref.Hash, [.. next.Staged.Waiting.Keys])]).ConfigureAwait(true);
        }
    }

    /// <summary>Reacts to an install denial signal from installs.</summary>
    [On]
    public async Task OnDenied(InstallDenied denied, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(denied);
        if (State.Staged is null) { return; }
        var next = State.Deny(denied.Alias, denied.PlanId);
        if (ReferenceEquals(next, State)) { return; }
        await Supersede(denied.Reason, ct).ConfigureAwait(true);
    }

    private async Task Promote(DeploymentState approved, CancellationToken ct)
    {
        var build = approved.Staged!.Build;
        var liveAliases = State.Live?.Aliases ?? [];
        var dropped = liveAliases.Except(build.Aliases, StringComparer.Ordinal).ToList();

        var installs = Context.Get<IInstalls>(new NeuronId(Id.Brain, "synapse.installs", "default"));
        var sources = Context.Get<ISources>(new NeuronId(Id.Brain, "synapse.sources", "default"));

        // 1. Retire dropped aliases first
        foreach (var alias in dropped)
        {
            await installs.Retire(alias, ct).ConfigureAwait(true);
        }

        // 2. Save promoted state with Deployed signal
        var now = Context.Time.GetUtcNow();
        var nextState = approved.Promote(now);
        await Save(nextState, [new Deployed(SourceKey, build.Ref.Hash, build.Aliases)]).ConfigureAwait(true);

        // 3. Update local silo hint
        var liveMap = live ?? Seat.Services.GetService(typeof(LiveBindings)) as LiveBindings;
        liveMap?.Set(Id.Brain, build.Ref);

        // 4. Rewire remaining/new aliases
        foreach (var alias in build.Aliases)
        {
            await installs.Rewire(alias, ct).ConfigureAwait(true);
        }

        // 5. Release dropped aliases in sources directory
        if (dropped.Count > 0)
        {
            await sources.Release(SourceKey, dropped, ct).ConfigureAwait(true);
        }
    }

    private async Task Supersede(string reason, CancellationToken ct)
    {
        if (State.Staged is not { } staged) { return; }
        var installs = Context.Get<IInstalls>(new NeuronId(Id.Brain, "synapse.installs", "default"));
        foreach (var alias in staged.Build.Aliases)
        {
            await installs.CancelPending(alias, ct).ConfigureAwait(true);
        }
        var next = State.Supersede();
        await Save(next, [new StageDropped(SourceKey, staged.Build.Ref.Hash, reason)]).ConfigureAwait(true);
    }

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<BuildRef?> Ref(UnitView view, CancellationToken ct = default) =>
        Task.FromResult(State.Ref(view));

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<StoredBuild?> Build(string hash, CancellationToken ct = default)
    {
        if (State.Live is { } liveBuild && string.Equals(liveBuild.Ref.Hash, hash, StringComparison.Ordinal))
        {
            return Task.FromResult<StoredBuild?>(liveBuild);
        }
        if (State.Staged is { } staged && string.Equals(staged.Build.Ref.Hash, hash, StringComparison.Ordinal))
        {
            return Task.FromResult<StoredBuild?>(staged.Build);
        }
        return Task.FromResult<StoredBuild?>(null);
    }

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<UnitSourceView?> Source(CancellationToken ct = default)
    {
        if (State.Live is not { } liveBuild)
        {
            return Task.FromResult<UnitSourceView?>(null);
        }
        return Task.FromResult<UnitSourceView?>(new UnitSourceView(
            SourceKey,
            liveBuild.Ref.Hash,
            liveBuild.Files,
            liveBuild.Icon,
            liveBuild.Aliases,
            State.At,
            liveBuild.RebuildReason));
    }

    /// <inheritdoc />
    [OneWayCall]
    public Task Refuse(string hash, string reason, CancellationToken ct = default)
    {
        var next = State.Refuse(hash, reason);
        if (ReferenceEquals(next, State)) { return Task.CompletedTask; }
        var signal = State.Staged is not null && string.Equals(State.Staged.Build.Ref.Hash, hash, StringComparison.Ordinal)
            ? (Signal)new StageDropped(SourceKey, hash, reason)
            : new BuildRefused(SourceKey, hash, reason);
        return Save(next, [signal]);
    }

    /// <inheritdoc />
    public async Task Retire(CancellationToken ct = default)
    {
        if (State.Live is null && State.Staged is null) { return; }
        var aliases = State.Live?.Aliases ?? [];
        var installs = Context.Get<IInstalls>(new NeuronId(Id.Brain, "synapse.installs", "default"));
        var sources = Context.Get<ISources>(new NeuronId(Id.Brain, "synapse.sources", "default"));

        if (State.Staged is { } staged)
        {
            foreach (var alias in staged.Build.Aliases)
            {
                await installs.CancelPending(alias, ct).ConfigureAwait(true);
            }
        }

        foreach (var alias in aliases)
        {
            await installs.Retire(alias, ct).ConfigureAwait(true);
        }

        var liveMap = live ?? Seat.Services.GetService(typeof(LiveBindings)) as LiveBindings;
        liveMap?.Retire(Id.Brain, SourceKey);

        await Save(State.Retire(), [new Undeployed(SourceKey)]).ConfigureAwait(true);
        if (aliases.Count > 0)
        {
            await sources.Release(SourceKey, aliases, ct).ConfigureAwait(true);
        }
    }
}
