using DigitalBrain.Units;
using DigitalBrain.Rules;

namespace DigitalBrain.Limits;

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

    /// <summary>
    /// Asks <paramref name="policies"/> to allow <paramref name="action"/>, runs <paramref name="call"/> when allowed, and releases the
    /// reservation when the call throws. A throw is read as "not dispatched"; a module whose call can end uncertain keeps its own
    /// Allow/Release so an unknown outcome stays counted.
    /// </summary>
    /// <typeparam name="T">The call's result.</typeparam>
    /// <param name="policies">The brain's policies neuron.</param>
    /// <param name="action">The declared action name.</param>
    /// <param name="amount">The non-negative amount for <see cref="PolicyMeasure.Sum"/> rules.</param>
    /// <param name="facts">Optional facts for conditions.</param>
    /// <param name="call">The external call.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The call's result.</returns>
    /// <exception cref="RuleViolationException">A policy refused the action; the call never ran.</exception>
    public static async Task<T> Guard<T>(this IPolicies policies, string action, decimal amount, IReadOnlyDictionary<string, string>? facts, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(call);
        var decision = await policies.Allow(action, amount, facts, ct).ConfigureAwait(false);
        Require(decision);
        try
        {
            return await call(ct).ConfigureAwait(false);
        }
        catch
        {
            await policies.Release(decision.DecisionId, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The <see cref="Guard{T}"/> of a call without a result.</summary>
    /// <param name="policies">The brain's policies neuron.</param>
    /// <param name="action">The declared action name.</param>
    /// <param name="amount">The non-negative amount for <see cref="PolicyMeasure.Sum"/> rules.</param>
    /// <param name="facts">Optional facts for conditions.</param>
    /// <param name="call">The external call.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="RuleViolationException">A policy refused the action; the call never ran.</exception>
    public static Task Guard(this IPolicies policies, string action, decimal amount, IReadOnlyDictionary<string, string>? facts, Func<CancellationToken, Task> call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        return policies.Guard(action, amount, facts, async token => { await call(token).ConfigureAwait(false); return true; }, ct);
    }
}
