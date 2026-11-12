using DigitalBrain.Confirmation;
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

namespace DigitalBrain.Units;

/// <summary>
/// The installs neuron of a brain: derives an app's needs from the catalog or proposed build, asks a person once per distinct needs,
/// records the approval with its bindings and schedules, and wires the host through <see cref="IInstallHooks"/>.
/// </summary>
/// <remarks>
/// An install is two-phase: <see cref="Install"/> records a <see cref="PendingInstall"/>, asks without awaiting the answer and returns
/// <see cref="InstallStatus.Pending"/>; the neuron's brain-wide synapse on <see cref="ConfirmationResolved"/> completes it. A plan id
/// that is not pending is ignored.
/// </remarks>
/// <param name="catalog">The synapse catalog source.</param>
/// <param name="hooks">The host's install hooks.</param>
/// <param name="units">The unit resolver for dynamic units, if available.</param>
[Neuron("synapse.installs")]
public sealed class InstallsNeuron(ISynapseCatalogSource catalog, IInstallHooks hooks, UnitResolver? units = null) : Neuron<InstallsState>, IInstalls
{
    /// <inheritdoc />
    public async Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (sentence, problem, sourceCatalog) = await Check(request, ct).ConfigureAwait(true);
        if (problem is not null)
        {
            return new InstallOutcome(InstallStatus.Refused, sentence, problem);
        }

        var alias = request.Alias;
        var bindings = request.Bindings ?? new Dictionary<string, string>();
        var needs = sourceCatalog!.NeedsOf(alias);
        var hash = needs.Hash();
        var existing = State.FindRecord(alias);
        if (existing is not null && existing.NeedsHash == hash)
        {
            var status = InstallStatus.Unchanged;
            if (!InstallsState.HasSameBindings(existing.Bindings, bindings))
            {
                await Save(State.Rebind(existing, bindings), [new Installed(alias, hash)]).ConfigureAwait(true);
                status = InstallStatus.Updated;
            }

            return new InstallOutcome(status, sentence, await Wire(alias, needs, ct, sourceCatalog).ConfigureAwait(true));
        }

        var plan = new ConfirmationPlan($"install:{alias}:{Guid.NewGuid():N}", ConfirmationAction.Other, Destructive: false, Title: alias, Body: sentence,
            ApproveLabel: "Allow", DenyLabel: "Deny", TargetLabel: alias)
        { RequiresPerson = true, Kind = ConfirmationPlan.SynapseInstallKind, Subject = alias };
        var pending = new PendingInstall(plan.Id, alias, new Dictionary<string, string>(bindings, StringComparer.Ordinal), hash, sentence);
        await Save(State.StagePending(pending), [new InstallRequested(alias, plan.Id)]).ConfigureAwait(true);
        var requests = Context.Get<IConfirmationRequests>(IConfirmationRequests.Of(Id.Brain));
        var immediate = await requests.Request(plan).ConfigureAwait(true);
        if (immediate is not null)
        {
            if (!immediate.Approved)
            {
                var reason = immediate.Outcome == ConfirmationOutcome.Expired ? ConfirmationDenyReason.Expired.ToString() : immediate.Reason.ToString();
                await Save(State.RemovePendingPlan(pending.PlanId), [new InstallDenied(alias, pending.PlanId, reason)]).ConfigureAwait(true);
                return new InstallOutcome(InstallStatus.Denied, sentence, reason);
            }
            var needsResolved = sourceCatalog.NeedsOf(alias);
            _ = await Wire(alias, needsResolved, ct, sourceCatalog).ConfigureAwait(true);
            await Save(State.CompleteInstall(pending, needsResolved, Context.Time.GetUtcNow()), [new Installed(alias, pending.NeedsHash)]).ConfigureAwait(true);
            return new InstallOutcome(InstallStatus.Installed, sentence, null);
        }
        return new InstallOutcome(InstallStatus.Pending, sentence, plan.Id);
    }

    /// <inheritdoc />
    public async Task<string?> Validate(InstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await Check(request, ct).ConfigureAwait(true)).Problem;
    }

    // Why the request would be refused (unknown alias, an unbound slot, a binding the host rejects), or null; with the needs sentence.
    private async Task<(string Sentence, string? Problem, SynapseCatalog? Source)> Check(InstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var alias = request.Alias;
        using var handle = units is not null
            ? (await units.Resolve(Id.Brain, alias, UnitView.Proposed, ct).ConfigureAwait(true)
               ?? await units.Resolve(Id.Brain, alias, UnitView.Live, ct).ConfigureAwait(true))
            : null;
        var source = handle?.Catalog ?? (catalog.Static.States.ContainsKey(alias) ? catalog.Static : null);
        if (alias is null || source is null || !source.States.ContainsKey(alias))
        {
            return (string.Empty, $"no synapse app '{alias}'", null);
        }

        var sentence = source.NeedsOf(alias).Sentence();
        var bindings = request.Bindings ?? new Dictionary<string, string>();
        if (source.NeedsOf(alias).Slots.FirstOrDefault(slot => !bindings.ContainsKey(slot.Name)) is { } unbound)
        {
            return (sentence, $"slot '{unbound.Name}' has no binding", source);
        }

        ArgumentNullException.ThrowIfNull(hooks);
        foreach (var slot in source.For(alias).SelectMany(d => d.Slots).DistinctBy(s => (s.Name, s.Contract)))
        {
            if (await hooks.ValidateBinding(StateOf(alias), slot, bindings[slot.Name], ct).ConfigureAwait(true) is { } problem)
            {
                return (sentence, problem, source);
            }
        }

        return (sentence, null, source);
    }

    /// <summary>Completes a pending install when its plan is resolved.</summary>
    /// <param name="resolved">The resolution.</param>
    /// <param name="ct">Cancellation.</param>
    [On]
    public async Task OnResolved(ConfirmationResolved resolved, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(catalog);
        if (State.FindPendingByPlan(resolved.PlanId) is not { } pending) { return; }
        var alias = pending.Alias;

        var requests = Context.Get<IConfirmationRequests>(IConfirmationRequests.Of(Id.Brain));
        var outcome = await requests.Outcome(resolved.PlanId).ConfigureAwait(true);
        if (outcome is null) { return; }

        if (!outcome.Approved)
        {
            var reason = outcome.Outcome == ConfirmationOutcome.Expired ? ConfirmationDenyReason.Expired.ToString() : outcome.Reason.ToString();
            await Save(State.RemovePendingPlan(pending.PlanId), [new InstallDenied(alias, pending.PlanId, reason)]).ConfigureAwait(true);
            return;
        }

        using var handle = units is not null
            ? (await units.Resolve(Id.Brain, alias, UnitView.Proposed, ct).ConfigureAwait(true)
               ?? await units.Resolve(Id.Brain, alias, UnitView.Live, ct).ConfigureAwait(true))
            : null;
        var source = handle?.Catalog ?? (catalog.Static.States.ContainsKey(alias) ? catalog.Static : null);
        if (source is null || !source.States.ContainsKey(alias))
        {
            await Save(State.RemovePendingPlan(pending.PlanId), [new InstallDenied(alias, pending.PlanId, "unknown alias")]).ConfigureAwait(true);
            return;
        }

        var needs = source.NeedsOf(alias);
        if (!string.Equals(needs.Hash(), pending.NeedsHash, StringComparison.Ordinal))
        {
            await Save(State.RemovePendingPlan(pending.PlanId), [new InstallDenied(alias, pending.PlanId, "superseded")]).ConfigureAwait(true);
            return;
        }

        _ = await Wire(alias, needs, ct, source).ConfigureAwait(true);
        await Save(State.CompleteInstall(pending, needs, Context.Time.GetUtcNow()), [new Installed(alias, pending.NeedsHash)]).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task Rewire(string alias, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (State.FindRecord(alias) is null) { return; }

        using var handle = units is not null
            ? await units.Resolve(Id.Brain, alias, UnitView.Live, ct).ConfigureAwait(true)
            : null;
        var source = handle?.Catalog ?? (catalog.Static.States.ContainsKey(alias) ? catalog.Static : null);
        if (source is not null && source.States.ContainsKey(alias))
        {
            var needs = source.NeedsOf(alias);
            await Wire(alias, needs, ct, source).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task CancelPending(string alias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alias);
        var (next, signals) = State.CancelPendingForAlias(alias, "superseded");
        if (signals.Length == 0) { return; }
        await Save(next, signals).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task Retire(string alias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        var (afterPending, signals) = State.CancelPendingForAlias(alias, "retired");
        if (signals.Length > 0)
        {
            await Save(afterPending, signals).ConfigureAwait(true);
        }

        if (State.FindRecord(alias) is null) { return; }
        await Save(State.RemoveRecord(alias), [new Retired(alias)]).ConfigureAwait(true);
        var state = StateOf(alias);
        try
        {
            await hooks.RemoveReactionEdges(state, ct).ConfigureAwait(true);
            await hooks.UnregisterSchedules(state, ct).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { _ = error; }
    }

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<InstallRecord?> Describe(string alias, CancellationToken ct) => Task.FromResult(State.FindRecord(alias));

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct) => Task.FromResult(State.Records);

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<IReadOnlyList<PendingInstall>> Pending(CancellationToken ct) => Task.FromResult(State.Pending);

    /// <inheritdoc />
    protected override Task OnActivate(CancellationToken cancellationToken)
        => Context.Synapses.Ensure<ConfirmationResolved>(IConfirmationRequests.Of(Id.Brain));

    private NeuronId StateOf(string alias) => new(Id.Brain, alias, "default");

    // Runs the hooks; a failure comes back as a reason. The next Install of the alias retries.
    private async Task<string?> Wire(string alias, SynapseNeeds needs, CancellationToken ct, SynapseCatalog? sourceCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        var state = StateOf(alias);
        var cat = sourceCatalog ?? catalog.Static;
        var reactions = cat.For(alias).Where(d => d.Kind == SynapseKind.Reaction).ToList();
        var signals = reactions.Where(d => d.SignalAlias is null).Select(d => d.Signal!).Distinct().ToList();
        var aliases = reactions.Select(d => d.SignalAlias).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        try
        {
            await hooks.EnsureReactionEdges(state, signals, aliases, ct).ConfigureAwait(true);
            await hooks.RegisterSchedules(state, needs.Schedules, ct).ConfigureAwait(true);
            return null;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return $"hooks failed: {error.Message}"; }
    }
}
