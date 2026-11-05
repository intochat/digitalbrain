namespace DigitalBrain.Testing;

/// <summary>Policy helpers for facts: a rule goes from proposal through the person's approval to the adopted list in one call.</summary>
public static class TestOSPolicyExtensions
{
    /// <summary>Proposes <paramref name="rule"/> to <paramref name="brainId"/>'s policies neuron, approves it as the person and returns the adopted rule.</summary>
    /// <param name="os">The test OS.</param>
    /// <param name="brainId">The brain id.</param>
    /// <param name="rule">The rule to adopt; its action must be declared by a registered module.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The adopted rule as the neuron lists it.</returns>
    /// <exception cref="InvalidOperationException">The proposal was refused before reaching the person.</exception>
    public static async Task<PolicyRule> AdoptPolicy(this TestOS os, string brainId, PolicyRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(rule);
        var policies = os.Get<IPolicies>(NeuronId.Parse(IPolicies.Key(brainId)));
        var outcome = await policies.Propose(rule, ct);
        if (outcome.Status != ProposalStatus.Pending)
        {
            throw new InvalidOperationException($"The proposal was {outcome.Status}: {outcome.Reason}");
        }

        await os.Confirmations.Approve(outcome.PlanId!);
        await os.Idle();
        return (await policies.List(ct)).Single(r => r.Id == rule.Id);
    }
}
