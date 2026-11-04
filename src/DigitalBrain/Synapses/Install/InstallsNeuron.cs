using DigitalBrain.Confirmation;
using DigitalBrain.Spi;

namespace DigitalBrain;

/// <summary>
/// The installs neuron of a brain: derives an app's needs from the catalog, asks a person once per distinct needs, records the
/// approval with its bindings and schedules, wires the host through <see cref="IInstallHooks"/>, and announces <see cref="GateChanged"/>.
/// </summary>
/// <param name="catalog">The synapse catalog.</param>
/// <param name="hooks">The host's install hooks.</param>
[Neuron("synapse.installs")]
public sealed class InstallsNeuron(SynapseCatalog catalog, IInstallHooks hooks) : Neuron<InstallsState>, IInstalls
{
    /// <inheritdoc />
    public async Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        var alias = request.Alias;
        if (alias is null || !catalog.States.ContainsKey(alias))
        {
            return new InstallOutcome(InstallStatus.Refused, string.Empty, $"no synapse app '{alias}'");
        }

        var needs = catalog.NeedsOf(alias);
        var sentence = needs.Sentence();
        var bindings = request.Bindings ?? new Dictionary<string, string>();
        if (needs.Slots.FirstOrDefault(slot => !bindings.ContainsKey(slot.Name)) is { } unbound)
        {
            return new InstallOutcome(InstallStatus.Refused, sentence, $"slot '{unbound.Name}' has no binding");
        }

        var hash = needs.Hash();
        var existing = Find(alias);
        if (existing is not null && existing.NeedsHash == hash)
        {
            await Wire(alias, needs, ct).ConfigureAwait(true);
            return new InstallOutcome(InstallStatus.Unchanged, sentence, null);
        }

        var plan = new ConfirmationPlan($"install:{alias}:{Guid.NewGuid():N}", ConfirmationAction.Other, Destructive: false, Title: alias, Body: sentence,
            ApproveLabel: "Allow", DenyLabel: "Deny", TargetLabel: alias) { RequiresPerson = true };
        var result = await Context.Confirm(plan, ct).ConfigureAwait(true);
        if (!result.Approved)
        {
            return new InstallOutcome(InstallStatus.Denied, sentence, result.Reason.ToString());
        }

        var record = new InstallRecord(alias, hash, new Dictionary<string, string>(bindings, StringComparer.Ordinal),
            [.. needs.Schedules.Select(s => s.Text)], Context.Time.GetUtcNow());
        await Save(new InstallsState([.. State.Records.Where(r => r.Alias != alias), record]), [new Installed(alias, hash)]).ConfigureAwait(true);
        await Wire(alias, needs, ct).ConfigureAwait(true);
        await PublishEphemeral(new GateChanged(Id.Brain.Value!)).ConfigureAwait(true);
        return new InstallOutcome(existing is null ? InstallStatus.Installed : InstallStatus.Updated, sentence, null);
    }

    /// <inheritdoc />
    public async Task Retire(string alias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        if (Find(alias) is null) { return; }
        var state = StateOf(alias);
        await hooks.RemoveReactionEdges(state, ct).ConfigureAwait(true);
        await hooks.UnregisterSchedules(state, ct).ConfigureAwait(true);
        await Save(new InstallsState([.. State.Records.Where(r => r.Alias != alias)]), [new Retired(alias)]).ConfigureAwait(true);
        await PublishEphemeral(new GateChanged(Id.Brain.Value!)).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task<InstallRecord?> Describe(string alias, CancellationToken ct) => Task.FromResult(Find(alias));

    /// <inheritdoc />
    public Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct) => Task.FromResult(State.Records);

    private InstallRecord? Find(string alias) => State.Records.FirstOrDefault(r => r.Alias == alias);

    private NeuronId StateOf(string alias) => new(Id.Brain, alias, "default");

    private async Task Wire(string alias, SynapseNeeds needs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        var state = StateOf(alias);
        var signals = catalog.For(alias).Where(d => d.Kind == SynapseKind.Reaction).Select(d => d.Signal!).Distinct().ToList();
        await hooks.EnsureReactionEdges(state, signals, ct).ConfigureAwait(true);
        await hooks.RegisterSchedules(state, needs.Schedules, ct).ConfigureAwait(true);
    }
}
