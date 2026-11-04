using DigitalBrain.Rules;

namespace DigitalBrain;

/// <summary>The verdict of <see cref="IPolicies.Allow"/> for an action call.</summary>
/// <param name="Allowed">True when every matching policy rule allowed the reservation; false when a rule refused it.</param>
/// <param name="DecisionId">The reservation id to pass to <see cref="IPolicies.Release"/> if the external call fails.</param>
/// <param name="RuleId">The id of the refusing rule when <paramref name="Allowed"/> is false; null otherwise.</param>
/// <param name="Reason">The rendered sentence of the refusing rule when <paramref name="Allowed"/> is false; null otherwise.</param>
public sealed record PolicyDecision(bool Allowed, string DecisionId, string? RuleId, string? Reason);

/// <summary>How a policy proposal ended or stands.</summary>
public enum ProposalStatus
{
    /// <summary>Waiting for the person's confirmation.</summary>
    Pending,

    /// <summary>Approved by the person and active in the brain's policies.</summary>
    Adopted,

    /// <summary>Denied or expired without adoption.</summary>
    Denied,

    /// <summary>Refused before asking the person (unknown action or invalid grammar).</summary>
    Refused,
}

/// <summary>The result of proposing a policy rule.</summary>
/// <param name="Status">The proposal status.</param>
/// <param name="RuleId">The rule id when <paramref name="Status"/> is not <see cref="ProposalStatus.Refused"/>; null otherwise.</param>
/// <param name="Text">The rendered policy sentence, or empty when refused before rendering.</param>
/// <param name="PlanId">The confirmation plan id when <paramref name="Status"/> is <see cref="ProposalStatus.Pending"/>; null otherwise.</param>
/// <param name="Reason">Why the proposal was refused, or null otherwise.</param>
public sealed record ProposalOutcome(ProposalStatus Status, string? RuleId, string Text, string? PlanId, string? Reason);

/// <summary>Enforces a <see cref="PolicyDecision"/> at a module boundary.</summary>
public static class PolicyGuard
{
    /// <summary>Throws <see cref="RuleViolationException"/> naming the refusing rule text when <paramref name="decision"/> is not allowed.</summary>
    /// <param name="decision">The decision returned by <see cref="IPolicies.Allow"/>.</param>
    /// <exception cref="RuleViolationException"><paramref name="decision"/> was refused.</exception>
    public static void Require(PolicyDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Allowed)
        {
            return;
        }

        var rule = decision.RuleId ?? "policy";
        var reason = decision.Reason ?? "refused by policy";
        throw new RuleViolationException(new Rejected(rule, [rule], [reason]));
    }
}
