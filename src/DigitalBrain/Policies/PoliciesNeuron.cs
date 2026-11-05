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
        var text = DeclaredActions.FirstOrDefault(a => string.Equals(a.Name, rule.Action, StringComparison.Ordinal)) is { } declared
            ? PolicyGrammar.Render(rule, declared)
            : rule.Text;
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
            State with { Pending = [.. State.Pending.Where(p => p.Rule.Id != prepared.Id), pending] },
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

        var rest = State.Pending.Where(p => p.PlanId != pending.PlanId).ToList();
        if (resolved.Outcome != ConfirmationOutcome.Approved)
        {
            await Save(State with { Pending = rest }, [new StateChanged(Id.ToString(), "policy.denied")]).ConfigureAwait(true);
            return;
        }

        var adopted = pending.Rule with { AdoptedAt = Context.Time.GetUtcNow() };
        await Save(
            State with { Rules = [.. State.Rules.Where(r => r.Id != adopted.Id), adopted], Pending = rest },
            [new PolicyAdopted(adopted.Id, adopted.Action, adopted.Text)]).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<PolicyDecision> Allow(string action, decimal amount, IReadOnlyDictionary<string, string>? facts, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var now = Context.Time.GetUtcNow();
        var decisionId = Guid.NewGuid().ToString("N");
        var matching = State.Rules
            .Where(r => string.Equals(r.Action, action, StringComparison.Ordinal) && Matches(r.Condition, facts))
            .ToList();

        foreach (var rule in matching)
        {
            var increment = IncrementOf(rule, amount);
            var used = WindowTotal(rule, now);
            if (used + increment > rule.Limit)
            {
                await Save(State, [new PolicyRefused(action, amount, rule.Id)]).ConfigureAwait(true);
                return new PolicyDecision(false, decisionId, rule.Id, rule.Text);
            }
        }

        if (matching.Count > 0)
        {
            var added = matching.Select(rule => new Reservation(decisionId, rule.Id, IncrementOf(rule, amount), now)).ToList();
            await Save(State with { Reservations = [.. State.Reservations, .. added] }, [new StateChanged(Id.ToString(), "policy.reserved")]).ConfigureAwait(true);
        }

        return new PolicyDecision(true, decisionId, null, null);
    }

    /// <inheritdoc />
    public Task Release(string decisionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        if (State.Reservations.All(r => r.DecisionId != decisionId))
        {
            return Task.CompletedTask;
        }

        return Save(State with { Reservations = [.. State.Reservations.Where(r => r.DecisionId != decisionId)] }, [new StateChanged(Id.ToString(), "policy.released")]);
    }

    /// <inheritdoc />
    public Task Retire(string ruleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        var rules = State.Rules.Where(r => r.Id != ruleId).ToList();
        var reservations = State.Reservations.Where(r => r.RuleId != ruleId).ToList();
        var pending = State.Pending.Where(p => p.Rule.Id != ruleId).ToList();
        if (rules.Count == State.Rules.Count && reservations.Count == State.Reservations.Count && pending.Count == State.Pending.Count)
        {
            return Task.CompletedTask;
        }

        return Save(new PoliciesState(rules, reservations, pending), [new PolicyRetired(ruleId)]);
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

    private static decimal IncrementOf(PolicyRule rule, decimal amount)
        => rule.Measure == PolicyMeasure.Count ? 1m : amount;

    private static bool Matches(PolicyCondition? condition, IReadOnlyDictionary<string, string>? facts)
    {
        if (condition is null)
        {
            return true;
        }

        // A fact the caller did not supply cannot prove the rule does not apply: fail closed.
        if (facts is null || !facts.TryGetValue(condition.Fact, out var actual) || actual is null)
        {
            return true;
        }

        return condition.Op switch
        {
            PolicyOp.Equals => string.Equals(actual, condition.Value, StringComparison.Ordinal),
            PolicyOp.Contains => actual.Contains(condition.Value, StringComparison.Ordinal),
            PolicyOp.StartsWith => actual.StartsWith(condition.Value, StringComparison.Ordinal),
            PolicyOp.EndsWith => actual.EndsWith(condition.Value, StringComparison.Ordinal),
            _ => false,
        };
    }

    private decimal WindowTotal(PolicyRule rule, DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        return rule.Window.Kind switch
        {
            PolicyWindowKind.None => 0m,
            PolicyWindowKind.Day => SumSince(rule.Id, new DateTimeOffset(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, TimeSpan.Zero), inclusive: true),
            PolicyWindowKind.Week => SumSince(rule.Id, StartOfUtcWeek(utcNow), inclusive: true),
            PolicyWindowKind.Month => SumSince(rule.Id, new DateTimeOffset(utcNow.Year, utcNow.Month, 1, 0, 0, 0, TimeSpan.Zero), inclusive: true),
            PolicyWindowKind.Rolling => SumSince(rule.Id, utcNow - (rule.Window.Span ?? TimeSpan.Zero), inclusive: false),
            _ => 0m,
        };
    }

    private decimal SumSince(string ruleId, DateTimeOffset threshold, bool inclusive)
    {
        var total = 0m;
        foreach (var reservation in State.Reservations)
        {
            if (reservation.RuleId != ruleId)
            {
                continue;
            }

            var inWindow = inclusive ? reservation.At >= threshold : reservation.At > threshold;
            if (inWindow)
            {
                total += reservation.Amount;
            }
        }

        return total;
    }

    private static DateTimeOffset StartOfUtcWeek(DateTimeOffset utcNow)
    {
        var diff = ((int)utcNow.DayOfWeek + 6) % 7;
        var monday = utcNow.UtcDateTime.Date.AddDays(-diff);
        return new DateTimeOffset(monday, TimeSpan.Zero);
    }
}
