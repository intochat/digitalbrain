using DigitalBrain.Contracts;

namespace DigitalBrain.Limits;

/// <summary>The policies neuron of one brain, at <c>{brain}/policies/default</c>.</summary>
[NeuronAlias("policies"), DefaultNeuronType("policies")]
public interface IPolicies
{
    /// <summary>The neuron key of <paramref name="brainId"/>'s policies neuron.</summary>
    /// <param name="brainId">The brain id.</param>
    /// <returns>The neuron key <c>{brainId}/policies/default</c>.</returns>
    static string Key(string brainId) => $"{brainId}/policies/default";

    /// <summary>
    /// Renders <paramref name="rule"/>'s sentence and asks the person through <c>IConfirmations</c> with <c>RequiresPerson = true</c>.
    /// Returns <see cref="ProposalStatus.Pending"/> until <c>ConfirmationResolved</c> adopts or denies it.
    /// </summary>
    /// <param name="rule">The proposed policy rule.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The pending proposal outcome.</returns>
    Task<ProposalOutcome> Propose(PolicyRule rule, CancellationToken ct = default);

    /// <summary>
    /// Evaluates every adopted rule whose <see cref="PolicyRule.Action"/> matches <paramref name="action"/> and whose
    /// <see cref="PolicyRule.Condition"/> holds against <paramref name="facts"/>, reserving <paramref name="amount"/> when allowed.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <param name="amount">The non-negative amount to reserve for <see cref="PolicyMeasure.Sum"/> rules.</param>
    /// <param name="facts">Optional string facts for condition evaluation. A conditional rule whose fact is absent applies.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Whether the action is allowed and the reservation's decision id.</returns>
    Task<PolicyDecision> Allow(string action, decimal amount, IReadOnlyDictionary<string, string>? facts, CancellationToken ct = default);

    /// <summary>Releases the reservations recorded under <paramref name="decisionId"/> when an external action fails.</summary>
    /// <param name="decisionId">The decision id returned by <see cref="Allow"/>.</param>
    /// <param name="ct">Cancellation.</param>
    Task Release(string decisionId, CancellationToken ct = default);

    /// <summary>Retires the rule with <paramref name="ruleId"/> and drops its reservations.</summary>
    /// <param name="ruleId">The rule id.</param>
    /// <param name="ct">Cancellation.</param>
    Task Retire(string ruleId, CancellationToken ct = default);

    /// <summary>Every adopted policy rule in this brain.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The adopted rules.</returns>
    [ReadOnlyCall]
    Task<IReadOnlyList<PolicyRule>> List(CancellationToken ct = default);

    /// <summary>Every action declared by the registered module manifests on this host.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The declared actions.</returns>
    [ReadOnlyCall]
    Task<IReadOnlyList<ActionDescriptor>> Actions(CancellationToken ct = default);
}
