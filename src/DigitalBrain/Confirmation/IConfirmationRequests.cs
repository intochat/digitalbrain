using DigitalBrain.Contracts;

namespace DigitalBrain.Confirmation;

/// <summary>
/// Requester contract for raising, querying, and withdrawing confirmation questions.
/// </summary>
[NeuronAlias("runtime.confirmations")]
public interface IConfirmationRequests
{
    /// <summary>Canonical NeuronId for a brain's confirmations neuron.</summary>
    static NeuronId Of(BrainId brain) => new(brain, "runtime.confirmations", IConfirmations.ConfirmationsNeuronKey);

    /// <summary>
    /// Raises a confirmation plan. Returns the immediate answer if skipped by policy or already known,
    /// or null if the plan is awaiting a person's decision.
    /// </summary>
    Task<ConfirmationResult?> Request(ConfirmationPlan plan);

    /// <summary>
    /// Queries the authoritative outcome for a given planId. Returns null if unknown or still pending.
    /// </summary>
    Task<ConfirmationResult?> Outcome(string planId);

    /// <summary>
    /// Withdraws a pending confirmation plan (e.g. on operation cancellation).
    /// </summary>
    Task Withdraw(string planId);
}
