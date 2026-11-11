using DigitalBrain.Confirmation;
using DigitalBrain.Neurons;

namespace DigitalBrain.Units;

/// <summary>
/// The installs neuron of a brain: derives an app's needs from the catalog, asks a person once per distinct needs, records the
/// approval with its bindings and schedules, and wires the host through <see cref="IInstallHooks"/>.
/// </summary>
/// <remarks>
/// An install is two-phase: <see cref="Install"/> records a <see cref="PendingInstall"/>, asks without awaiting the answer and returns
/// <see cref="InstallStatus.Pending"/>; the neuron's brain-wide synapse on <see cref="ConfirmationResolved"/> completes it. A plan id
/// that is not pending is ignored.
/// </remarks>
/// <param name="catalog">
/// The live synapse catalog: needs and bindings come from <see cref="ISynapseCatalogSource.ForNeeds"/> (a staged build of the alias, else
/// the current catalog); reaction edges from <see cref="ISynapseCatalogSource.Current"/>.
/// </param>
/// <param name="hooks">The host's install hooks.</param>
[Neuron("synapse.installs")]
public sealed class InstallsNeuron(ISynapseCatalogSource catalog, IInstallHooks hooks) : Neuron<InstallsState>, IInstalls, IInstallsCallbacks
{
    // Approvals waiting for their alias's source to be restored, by the one-use proof handed to the detached restore.
    private readonly Dictionary<string, ConfirmationResolved> _restoring = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (sentence, problem) = await Check(request, ct).ConfigureAwait(true);
        if (problem is not null)
        {
            return new InstallOutcome(InstallStatus.Refused, sentence, problem);
        }

        var alias = request.Alias;
        var bindings = request.Bindings ?? new Dictionary<string, string>();
        var needs = catalog.ForNeeds(alias).NeedsOf(alias);
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

            return new InstallOutcome(status, sentence, await Wire(alias, needs, ct).ConfigureAwait(true));
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
            var source = catalog.ForNeeds(alias);
            var needsResolved = source.NeedsOf(alias);
            _ = await Wire(alias, needsResolved, ct).ConfigureAwait(true);
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
    private async Task<(string Sentence, string? Problem)> Check(InstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var alias = request.Alias;
        var source = alias is null ? null : catalog.ForNeeds(alias);
        if (alias is null || source is null || !source.States.ContainsKey(alias))
        {
            return (string.Empty, $"no synapse app '{alias}'");
        }

        var sentence = source.NeedsOf(alias).Sentence();
        var bindings = request.Bindings ?? new Dictionary<string, string>();
        if (source.NeedsOf(alias).Slots.FirstOrDefault(slot => !bindings.ContainsKey(slot.Name)) is { } unbound)
        {
            return (sentence, $"slot '{unbound.Name}' has no binding");
        }

        ArgumentNullException.ThrowIfNull(hooks);
        foreach (var slot in source.For(alias).SelectMany(d => d.Slots).DistinctBy(s => (s.Name, s.Contract)))
        {
            if (await hooks.ValidateBinding(StateOf(alias), slot, bindings[slot.Name], ct).ConfigureAwait(true) is { } problem)
            {
                return (sentence, problem);
            }
        }

        return (sentence, null);
    }

    /// <summary>Completes a pending install when its plan is resolved.</summary>
    /// <param name="resolved">The resolution.</param>
    /// <param name="ct">Cancellation.</param>
    [On]
    public Task OnResolved(ConfirmationResolved resolved, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        return Judge(resolved, restore: true, ct);
    }

    /// <inheritdoc />
    async Task IInstallsCallbacks.Restored(string proof)
    {
        ConfirmationResolved? resolved;
        lock (_restoring)
        {
            resolved = _restoring.Remove(proof ?? "", out var found) ? found : null;
        }

        if (resolved is null)
        {
            throw new InvalidOperationException("Restored was called without a live proof.");
        }

        await Judge(resolved, restore: false, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task Judge(ConfirmationResolved resolved, bool restore, CancellationToken ct)
    {
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

        // After a restart the alias's source may not be live yet. The restore takes the synapse host's gate, and a deploy holding that gate
        // may be waiting on this neuron, so the turn never awaits it: the ask stays pending, a detached restore runs, and its one-way
        // callback judges the recorded answer again in a turn of its own. Approving an ask raised before a restart is never denied as unknown.
        if (restore && !catalog.ForNeeds(alias).States.ContainsKey(alias))
        {
            var restorer = Seat.Services.GetService(typeof(ISynapseRestorer)) as ISynapseRestorer ?? NoSynapseRestorer.Instance;
            var proof = Guid.NewGuid().ToString("N");
            lock (_restoring) { _restoring[proof] = resolved; }
            _ = RestoreThenJudge(restorer, alias, proof);
            return;
        }

        var source = catalog.ForNeeds(alias);
        if (!source.States.ContainsKey(alias))
        {
            await Save(State.RemovePendingPlan(pending.PlanId), [new InstallDenied(alias, pending.PlanId, "unknown alias")]).ConfigureAwait(true);
            return;
        }

        var needs = source.NeedsOf(alias);
        _ = await Wire(alias, needs, ct).ConfigureAwait(true);
        await Save(State.CompleteInstall(pending, needs, Context.Time.GetUtcNow()), [new Installed(alias, pending.NeedsHash)]).ConfigureAwait(true);
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
    /// <remarks>
    /// Pending asks of the alias are denied with reason <c>retired</c>. The record is removed and <see cref="Retired"/> published first; a failing hook is then swallowed, since
    /// <see cref="IInstalls.Retire"/> reports nothing, and leaves edges or schedules the host must tolerate (they reach a retired app).
    /// </remarks>
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
    public Task<InstallRecord?> Describe(string alias, CancellationToken ct) => Task.FromResult(State.FindRecord(alias));

    /// <inheritdoc />
    public Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct) => Task.FromResult(State.Records);

    /// <inheritdoc />
    public Task<IReadOnlyList<PendingInstall>> Pending(CancellationToken ct) => Task.FromResult(State.Pending);

    /// <inheritdoc />
    protected override Task OnActivate(CancellationToken cancellationToken)
        => Context.Synapses.Ensure<ConfirmationResolved>(IConfirmationRequests.Of(Id.Brain));

    // Detached from the turn: restores the alias, then posts the recorded answer back through the neuron's own mailbox.
    private async Task RestoreThenJudge(ISynapseRestorer restorer, string alias, string proof)
    {
        try { await InstallRestore.EnsureInstalledAliases(catalog, [alias], restorer, Id.Brain, CancellationToken.None).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException) { _ = error; } // judged as it is: an alias still missing is denied as unknown

        try { await Context.Get<IInstallsCallbacks>(Id).Restored(proof).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The ask stays pending; a later answer to a new ask of the alias completes it.
            _ = error;
            lock (_restoring) { _restoring.Remove(proof); }
        }
    }

    private NeuronId StateOf(string alias) => new(Id.Brain, alias, "default");

    // Runs the hooks; a failure comes back as a reason. The next Install of the alias retries.
    private async Task<string?> Wire(string alias, SynapseNeeds needs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        var state = StateOf(alias);
        var reactions = catalog.Current.For(alias).Where(d => d.Kind == SynapseKind.Reaction).ToList();
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
