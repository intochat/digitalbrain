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
}
