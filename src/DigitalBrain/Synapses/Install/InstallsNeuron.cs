using DigitalBrain.Confirmation;
using DigitalBrain.Spi;

namespace DigitalBrain;

/// <summary>
/// The installs neuron of a brain: derives an app's needs from the catalog, asks a person once per distinct needs, records the
/// approval with its bindings and schedules, wires the host through <see cref="IInstallHooks"/>, and announces <see cref="GateChanged"/>.
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
public sealed class InstallsNeuron(ISynapseCatalogSource catalog, IInstallHooks hooks) : Neuron<InstallsState>, IInstalls
{
    /// <inheritdoc />
    public async Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        var alias = request.Alias;
        var source = alias is null ? null : catalog.ForNeeds(alias);
        if (alias is null || source is null || !source.States.ContainsKey(alias))
        {
            return new InstallOutcome(InstallStatus.Refused, string.Empty, $"no synapse app '{alias}'");
        }

        var needs = source.NeedsOf(alias);
        var sentence = needs.Sentence();
        var bindings = request.Bindings ?? new Dictionary<string, string>();
        if (needs.Slots.FirstOrDefault(slot => !bindings.ContainsKey(slot.Name)) is { } unbound)
        {
            return new InstallOutcome(InstallStatus.Refused, sentence, $"slot '{unbound.Name}' has no binding");
        }

        ArgumentNullException.ThrowIfNull(hooks);
        foreach (var slot in source.For(alias).SelectMany(d => d.Slots).DistinctBy(s => (s.Name, s.Contract)))
        {
            if (await hooks.ValidateBinding(StateOf(alias), slot, bindings[slot.Name], ct).ConfigureAwait(true) is { } problem)
            {
                return new InstallOutcome(InstallStatus.Refused, sentence, problem);
            }
        }

        var hash = needs.Hash();
        var existing = Find(alias);
        if (existing is not null && existing.NeedsHash == hash)
        {
            var status = InstallStatus.Unchanged;
            if (!SameBindings(existing.Bindings, bindings))
            {
                // The person approved these needs; a new binding of the same slots is saved without asking again.
                var rebound = existing with { Bindings = new Dictionary<string, string>(bindings, StringComparer.Ordinal) };
                await Save(State with { Records = [.. State.Records.Where(r => r.Alias != alias), rebound] }, [new Installed(alias, hash)]).ConfigureAwait(true);
                status = InstallStatus.Updated;
            }

            await GateChanged().ConfigureAwait(true);
            return new InstallOutcome(status, sentence, await Wire(alias, needs, ct).ConfigureAwait(true));
        }

        var plan = new ConfirmationPlan($"install:{alias}:{Guid.NewGuid():N}", ConfirmationAction.Other, Destructive: false, Title: alias, Body: sentence,
            ApproveLabel: "Allow", DenyLabel: "Deny", TargetLabel: alias)
        { RequiresPerson = true };
        var pending = new PendingInstall(plan.Id, alias, new Dictionary<string, string>(bindings, StringComparer.Ordinal), hash, sentence);
        // A newer ask of an alias replaces the older one; the older plan's answer is then ignored.
        await Save(State with { Pending = [.. State.Pending.Where(p => p.Alias != alias), pending] }, [new InstallRequested(alias, plan.Id)]).ConfigureAwait(true);
        _ = Ask(plan);
        return new InstallOutcome(InstallStatus.Pending, sentence, plan.Id);
    }

    /// <summary>Completes a pending install when its plan is resolved.</summary>
    /// <param name="resolved">The resolution.</param>
    /// <param name="ct">Cancellation.</param>
    [On]
    public async Task OnResolved(ConfirmationResolved resolved, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(catalog);
        if (State.Pending.FirstOrDefault(p => p.PlanId == resolved.PlanId) is not { } pending) { return; }
        var rest = State.Pending.Where(p => p.PlanId != pending.PlanId).ToList();
        var alias = pending.Alias;
        if (resolved.Outcome != ConfirmationOutcome.Approved)
        {
            var reason = resolved.Outcome == ConfirmationOutcome.Expired ? ConfirmationDenyReason.Expired.ToString() : resolved.Reason.ToString();
            await Save(State with { Pending = rest }, [new InstallDenied(alias, pending.PlanId, reason)]).ConfigureAwait(true);
            return;
        }

        // The catalog may have changed since the ask (a new build without the app): the ask is dropped, not left pending.
        var source = catalog.ForNeeds(alias);
        if (!source.States.ContainsKey(alias))
        {
            await Save(State with { Pending = rest }, [new InstallDenied(alias, pending.PlanId, "unknown alias")]).ConfigureAwait(true);
            return;
        }

        var needs = source.NeedsOf(alias);
        var record = new InstallRecord(alias, pending.NeedsHash, pending.Bindings, [.. needs.Schedules.Select(s => s.Text)], Context.Time.GetUtcNow());
        await Save(new InstallsState([.. State.Records.Where(r => r.Alias != alias), record], rest), [new Installed(alias, pending.NeedsHash)]).ConfigureAwait(true);
        await GateChanged().ConfigureAwait(true);
        // A failing hook leaves the record standing; the next Install of the alias (Unchanged) runs the hooks again and reports it.
        _ = await Wire(alias, needs, ct).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task CancelPending(string alias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alias);
        var cancelled = State.Pending.Where(p => p.Alias == alias).ToList();
        if (cancelled.Count == 0) { return; }
        await Save(State with { Pending = [.. State.Pending.Where(p => p.Alias != alias)] },
            [.. cancelled.Select(p => (Signal)new InstallDenied(alias, p.PlanId, "superseded"))]).ConfigureAwait(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pending asks of the alias are denied with reason <c>retired</c>. The record is removed and <see cref="DigitalBrain.GateChanged"/> published first; a failing hook is then swallowed, since
    /// <see cref="IInstalls.Retire"/> reports nothing, and leaves edges or schedules the host must tolerate (they reach a retired app).
    /// </remarks>
    public async Task Retire(string alias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        // Every pending ask of the alias is cancelled, record or not; a later answer to it is then ignored.
        var cancelled = State.Pending.Where(p => p.Alias == alias).ToList();
        if (cancelled.Count > 0)
        {
            await Save(State with { Pending = [.. State.Pending.Where(p => p.Alias != alias)] },
                [.. cancelled.Select(p => (Signal)new InstallDenied(alias, p.PlanId, "retired"))]).ConfigureAwait(true);
        }

        if (Find(alias) is null) { return; }
        await Save(State with { Records = [.. State.Records.Where(r => r.Alias != alias)] }, [new Retired(alias)]).ConfigureAwait(true);
        await GateChanged().ConfigureAwait(true);
        var state = StateOf(alias);
        try
        {
            await hooks.RemoveReactionEdges(state, ct).ConfigureAwait(true);
            await hooks.UnregisterSchedules(state, ct).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { _ = error; }
    }

    /// <inheritdoc />
    public Task<InstallRecord?> Describe(string alias, CancellationToken ct) => Task.FromResult(Find(alias));

    /// <inheritdoc />
    public Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct) => Task.FromResult(State.Records);

    /// <inheritdoc />
    public Task<IReadOnlyList<PendingInstall>> Pending(CancellationToken ct) => Task.FromResult(State.Pending);

    /// <inheritdoc />
    protected override Task OnActivate(CancellationToken cancellationToken) => Context.Synapses.EnsureBrainWide(typeof(ConfirmationResolved));

    // The answer arrives as ConfirmationResolved; the awaited result is not used, and a failing ask leaves the install pending.
    private async Task Ask(ConfirmationPlan plan)
    {
        try { _ = await Context.Confirm(plan, CancellationToken.None).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException) { _ = error; }
    }

    private static bool SameBindings(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private InstallRecord? Find(string alias) => State.Records.FirstOrDefault(r => r.Alias == alias);

    private NeuronId StateOf(string alias) => new(Id.Brain, alias, "default");

    private Task GateChanged() => PublishEphemeral(new GateChanged(Id.Brain.Value!));

    // Runs the hooks; a failure comes back as a reason. The next Install of the alias retries.
    private async Task<string?> Wire(string alias, SynapseNeeds needs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        var state = StateOf(alias);
        var signals = catalog.Current.For(alias).Where(d => d.Kind == SynapseKind.Reaction).Select(d => d.Signal!).Distinct().ToList();
        try
        {
            await hooks.EnsureReactionEdges(state, signals, ct).ConfigureAwait(true);
            await hooks.RegisterSchedules(state, needs.Schedules, ct).ConfigureAwait(true);
            return null;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return $"hooks failed: {error.Message}"; }
    }
}
