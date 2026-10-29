namespace DigitalBrain.Confirmation;

/// <summary>
/// The confirmation gate (07 D-03): asks a person before something runs. <see cref="Confirm"/> applies the brain's
/// <see cref="ConfirmationPolicy"/>; when it asks it publishes <see cref="ConfirmationRequested"/> and waits until a person answers
/// or the plan's expiry (5 minutes) passes. Every plan ends with one <see cref="ConfirmationResolved"/>, skipped ones included.
/// The runtime hosts the answering side as the neuron <see cref="ConfirmationsNeuronKey"/>; UI buttons target it, never the app.
/// </summary>
public interface IConfirmations
{
    /// <summary>The key of the runtime confirmations neuron that approve and deny buttons call (07 D-19).</summary>
    public const string ConfirmationsNeuronKey = "confirmations";

    /// <summary>The operation the approve and deny buttons call on <see cref="ConfirmationsNeuronKey"/>.</summary>
    public const string ResolveOperation = "Resolve";

    /// <summary>The policy of the brain this instance serves.</summary>
    ConfirmationPolicy Policy { get; }

    /// <summary>Asks about <paramref name="plan"/> under the brain policy and completes with the answer. The requester is the caller the runtime stamped.</summary>
    Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default);

    /// <summary>
    /// Answers the pending plan <paramref name="planId"/>. The approver is the caller the runtime stamped on this call, never an
    /// argument: approval requires a human principal (07 D-09), so an app or agent approving, including an app relaying a click,
    /// ends the plan <see cref="ConfirmationOutcome.Denied"/> with <see cref="ConfirmationDenyReason.SelfApproval"/>. The first
    /// answer wins; later answers to the same plan are ignored, and so are unknown ids.
    /// </summary>
    Task Resolve(string planId, bool approve, CancellationToken ct = default);
}
