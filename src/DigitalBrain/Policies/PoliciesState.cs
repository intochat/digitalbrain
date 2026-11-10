namespace DigitalBrain;

/// <summary>A reservation recorded against an adopted <see cref="PolicyRule"/>.</summary>
/// <param name="DecisionId">The decision id shared by all reservations of one <see cref="IPolicies.Allow"/> call.</param>
/// <param name="RuleId">The rule id the reservation counts against.</param>
/// <param name="Amount">The reserved amount (1 for <see cref="PolicyMeasure.Count"/>, the call's amount for <see cref="PolicyMeasure.Sum"/>).</param>
/// <param name="At">When the reservation was made.</param>
public sealed record Reservation(string DecisionId, string RuleId, decimal Amount, DateTimeOffset At);

/// <summary>A proposed policy rule awaiting confirmation from a person.</summary>
/// <param name="PlanId">The confirmation plan id.</param>
/// <param name="Rule">The proposed rule.</param>
public sealed record PendingProposal(string PlanId, PolicyRule Rule);

/// <summary>The persisted state of a brain's <see cref="PoliciesNeuron"/>.</summary>
/// <param name="Rules">The adopted policy rules.</param>
/// <param name="Reservations">The active reservations against adopted rules.</param>
/// <param name="Pending">The proposals waiting for a person's confirmation.</param>
public sealed record PoliciesState(
    IReadOnlyList<PolicyRule> Rules,
    IReadOnlyList<Reservation> Reservations,
    IReadOnlyList<PendingProposal> Pending)
{
    /// <summary>An empty policy state.</summary>
    public PoliciesState() : this([], [], []) { }

    internal PoliciesState StageProposal(PendingProposal proposal)
        => this with { Pending = [.. Pending.Where(p => p.Rule.Id != proposal.Rule.Id), proposal] };

    internal PoliciesState DenyProposal(string planId)
        => this with { Pending = [.. Pending.Where(p => p.PlanId != planId)] };

    internal (PoliciesState Next, PolicyRule Adopted) AdoptProposal(PendingProposal pending, DateTimeOffset now)
    {
        var adopted = pending.Rule with { AdoptedAt = now };
        IReadOnlyList<PolicyRule> rules = [.. Rules.Where(r => r.Id != adopted.Id), adopted];
        var reservations = PruneReservations(Reservations.Where(r => r.RuleId != adopted.Id), rules, now);
        var rest = Pending.Where(p => p.PlanId != pending.PlanId).ToList();
        return (new PoliciesState(rules, reservations, rest), adopted);
    }

    internal (PoliciesState? Next, PolicyRule? ViolatedRule) TryReserve(
        string decisionId,
        string action,
        decimal amount,
        IReadOnlyDictionary<string, string>? facts,
        DateTimeOffset now)
    {
        var matching = Rules.Where(r => r.AppliesTo(action, facts)).ToList();
        var utcNow = now.ToUniversalTime();

        foreach (var rule in matching)
        {
            var increment = rule.IncrementFor(amount);
            var used = Reservations
                .Where(r => r.RuleId == rule.Id && rule.Window.Contains(r.At, utcNow))
                .Sum(r => r.Amount);
            if (used + increment > rule.Limit)
            {
                return (this with { Reservations = PruneReservations(Reservations, Rules, now) }, rule);
            }
        }

        if (matching.Count == 0)
        {
            return (null, null);
        }

        var added = matching.Select(rule => new Reservation(decisionId, rule.Id, rule.IncrementFor(amount), now));
        return (this with { Reservations = PruneReservations([.. Reservations, .. added], Rules, now) }, null);
    }

    internal PoliciesState? ReleaseReservation(string decisionId, DateTimeOffset now)
    {
        if (Reservations.All(r => r.DecisionId != decisionId))
        {
            return null;
        }

        return this with { Reservations = PruneReservations(Reservations.Where(r => r.DecisionId != decisionId), Rules, now) };
    }

    internal PoliciesState? RetireRule(string ruleId)
    {
        var rules = Rules.Where(r => r.Id != ruleId).ToList();
        var reservations = Reservations.Where(r => r.RuleId != ruleId).ToList();
        var pending = Pending.Where(p => p.Rule.Id != ruleId).ToList();
        if (rules.Count == Rules.Count && reservations.Count == Reservations.Count && pending.Count == Pending.Count)
        {
            return null;
        }

        return new PoliciesState(rules, reservations, pending);
    }

    private static IReadOnlyList<Reservation> PruneReservations(IEnumerable<Reservation> reservations, IReadOnlyList<PolicyRule> rules, DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        var live = rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        return [.. reservations.Where(r => live.TryGetValue(r.RuleId, out var rule) && rule.Window.Contains(r.At, utcNow))];
    }
}
