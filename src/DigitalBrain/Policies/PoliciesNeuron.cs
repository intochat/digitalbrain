using DigitalBrain.Confirmation;

namespace DigitalBrain;

/// <summary>
/// The per-brain policies neuron (<c>{brain}/policies/default</c>): asks a person before adopting a promise, reserves against adopted
/// rules on each <see cref="Allow"/> call, releases reservations when an external call fails, and retires rules on request.
/// </summary>
[Neuron("policies")]
public sealed class PoliciesNeuron : Neuron<PoliciesState>, IPolicies
{
    /// <inheritdoc />
    public async Task<ProposalOutcome> Propose(PolicyRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            return new ProposalOutcome(ProposalStatus.Refused, null, string.Empty, null, "grammar: rule id must not be empty.");
        }

        if (DeclaredActions.FirstOrDefault(a => string.Equals(a.Name, rule.Action, StringComparison.Ordinal)) is not { } declared)
        {
            return new ProposalOutcome(ProposalStatus.Refused, rule.Id, string.Empty, null, $"grammar: action '{rule.Action}' is not declared by any module.");
        }

        string text;
        try
        {
            text = PolicyGrammar.Render(rule, declared);
        }
        catch (PolicyGrammarException error)
        {
            return new ProposalOutcome(ProposalStatus.Refused, rule.Id, string.Empty, null, error.Message);
        }

        var prepared = rule with { Text = text };
        var plan = new ConfirmationPlan(
            $"policy:{prepared.Id}:{Guid.NewGuid():N}",
            ConfirmationAction.Other,
            Destructive: false,
            Title: "Adopt a promise",
            Body: text,
            ApproveLabel: "Allow",
            DenyLabel: "Deny",
            TargetLabel: prepared.Action)
        {
            RequiresPerson = true,
        };
        var pending = new PendingProposal(plan.Id, prepared);
        await Save(
            State.StageProposal(pending),
            [new PolicyProposed(prepared.Id, plan.Id, text)]).ConfigureAwait(true);
        _ = Ask(plan);
        return new ProposalOutcome(ProposalStatus.Pending, prepared.Id, text, plan.Id, null);
    }

    /// <summary>Completes a pending proposal when its confirmation plan is resolved.</summary>
    /// <param name="resolved">The confirmation resolution.</param>
    /// <param name="ct">Cancellation.</param>
    [On]
    public async Task OnResolved(ConfirmationResolved resolved, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (State.Pending.FirstOrDefault(p => p.PlanId == resolved.PlanId) is not { } pending)
        {
            return;
        }

        if (resolved.Outcome != ConfirmationOutcome.Approved)
        {
            await Save(State.DenyProposal(pending.PlanId), [new StateChanged(Id.ToString(), "policy.denied")]).ConfigureAwait(true);
            return;
        }

        var (next, adopted) = State.AdoptProposal(pending, Context.Time.GetUtcNow());
        await Save(next, [new PolicyAdopted(adopted.Id, adopted.Action, adopted.Text)]).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<PolicyDecision> Allow(string action, decimal amount, IReadOnlyDictionary<string, string>? facts, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var decisionId = Guid.NewGuid().ToString("N");
        var (next, violated) = State.TryReserve(decisionId, action, amount, facts, Context.Time.GetUtcNow());

        if (violated is not null)
        {
            await Save(next!, [new PolicyRefused(action, amount, violated.Id)]).ConfigureAwait(true);
            return new PolicyDecision(false, decisionId, violated.Id, violated.Text);
        }

        if (next is not null)
        {
            await Save(next, [new StateChanged(Id.ToString(), "policy.reserved")]).ConfigureAwait(true);
        }

        return new PolicyDecision(true, decisionId, null, null);
    }

    /// <inheritdoc />
    public Task Release(string decisionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        return State.ReleaseReservation(decisionId, Context.Time.GetUtcNow()) is { } next
            ? Save(next, [new StateChanged(Id.ToString(), "policy.released")])
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Retire(string ruleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        return State.RetireRule(ruleId) is { } next
            ? Save(next, [new PolicyRetired(ruleId)])
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PolicyRule>> List(CancellationToken ct = default) => Task.FromResult(State.Rules);

    /// <inheritdoc />
    public Task<IReadOnlyList<ActionDescriptor>> Actions(CancellationToken ct = default) => Task.FromResult(DeclaredActions);

    /// <inheritdoc />
    protected override Task OnActivate(CancellationToken cancellationToken) => Context.Synapses.EnsureBrainWide(typeof(ConfirmationResolved));

    private IReadOnlyList<ActionDescriptor> DeclaredActions
        => Context.Services.GetService(typeof(IReadOnlyList<ActionDescriptor>)) as IReadOnlyList<ActionDescriptor> ?? [];

    private async Task Ask(ConfirmationPlan plan)
    {
        try { _ = await Context.Confirm(plan, CancellationToken.None).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException) { _ = error; }
    }
}
