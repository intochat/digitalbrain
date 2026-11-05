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

        // Nothing reaches the person unless a module declared the action and the row renders under the grammar.
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

        var now = Context.Time.GetUtcNow();
        var adopted = pending.Rule with { AdoptedAt = now };
        var rules = (IReadOnlyList<PolicyRule>)[.. State.Rules.Where(r => r.Id != adopted.Id), adopted];
        await Save(
            State with { Rules = rules, Reservations = Pruned(State.Reservations.Where(r => r.RuleId != adopted.Id), rules, now), Pending = rest },
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
                await Save(State with { Reservations = Pruned(State.Reservations, State.Rules, now) }, [new PolicyRefused(action, amount, rule.Id)]).ConfigureAwait(true);
                return new PolicyDecision(false, decisionId, rule.Id, rule.Text);
            }
        }

        if (matching.Count > 0)
        {
            var added = matching.Select(rule => new Reservation(decisionId, rule.Id, IncrementOf(rule, amount), now)).ToList();
            await Save(State with { Reservations = Pruned([.. State.Reservations, .. added], State.Rules, now) }, [new StateChanged(Id.ToString(), "policy.reserved")]).ConfigureAwait(true);
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

        var now = Context.Time.GetUtcNow();
        return Save(State with { Reservations = Pruned(State.Reservations.Where(r => r.DecisionId != decisionId), State.Rules, now) }, [new StateChanged(Id.ToString(), "policy.released")]);
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
        var total = 0m;
        foreach (var reservation in State.Reservations)
        {
            if (reservation.RuleId == rule.Id && InWindow(rule, reservation, utcNow))
            {
                total += reservation.Amount;
            }
        }

        return total;
    }

    // Reservations outside their rule's window, or without a live rule, can never count again: every save drops them,
    // so the state stays bounded by one window of traffic per rule. A None window accumulates nothing at all.
    private static IReadOnlyList<Reservation> Pruned(IEnumerable<Reservation> reservations, IReadOnlyList<PolicyRule> rules, DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        var live = rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        return [.. reservations.Where(r => live.TryGetValue(r.RuleId, out var rule) && InWindow(rule, r, utcNow))];
    }

    private static bool InWindow(PolicyRule rule, Reservation reservation, DateTimeOffset utcNow) => rule.Window.Kind switch
    {
        PolicyWindowKind.None => false,
        PolicyWindowKind.Day => reservation.At >= new DateTimeOffset(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, TimeSpan.Zero),
        PolicyWindowKind.Week => reservation.At >= StartOfUtcWeek(utcNow),
        PolicyWindowKind.Month => reservation.At >= new DateTimeOffset(utcNow.Year, utcNow.Month, 1, 0, 0, 0, TimeSpan.Zero),
        PolicyWindowKind.Rolling => reservation.At > utcNow - (rule.Window.Span ?? TimeSpan.Zero),
        _ => false,
    };

    private static DateTimeOffset StartOfUtcWeek(DateTimeOffset utcNow)
    {
        var diff = ((int)utcNow.DayOfWeek + 6) % 7;
        var monday = utcNow.UtcDateTime.Date.AddDays(-diff);
        return new DateTimeOffset(monday, TimeSpan.Zero);
    }
}
