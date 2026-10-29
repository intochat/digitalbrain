namespace DigitalBrain.Confirmation;

/// <summary>A plan waits for a person's answer.</summary>
/// <param name="PlanId">The plan id.</param>
/// <param name="Plan">The plan.</param>
/// <param name="Requester">Who asked (the app or neuron, stamped by the runtime).</param>
/// <param name="Policy">The brain policy the plan was asked under.</param>
/// <param name="ExpiresAt">When it expires.</param>
[Signal("confirmation.requested")]
public sealed record ConfirmationRequested(string PlanId, ConfirmationPlan Plan, ConfirmationPrincipal Requester, ConfirmationPolicy Policy, DateTimeOffset ExpiresAt) : Signal;

/// <summary>A plan was answered, expired or skipped by policy. Written for every plan: the audit record (T-07-07).</summary>
/// <param name="PlanId">The plan id.</param>
/// <param name="Action">What the plan would do.</param>
/// <param name="TargetLabel">The display name of what is acted on.</param>
/// <param name="Requester">Who asked.</param>
/// <param name="Approver">Who answered; null when expired or skipped.</param>
/// <param name="Policy">The brain policy.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Reason">Why it was not approved.</param>
/// <param name="Skipped">True when the policy approved without asking.</param>
/// <param name="At">When it ended.</param>
[Signal("confirmation.resolved")]
public sealed record ConfirmationResolved(
    string PlanId,
    ConfirmationAction Action,
    string TargetLabel,
    ConfirmationPrincipal Requester,
    ConfirmationPrincipal? Approver,
    ConfirmationPolicy Policy,
    ConfirmationOutcome Outcome,
    ConfirmationDenyReason Reason,
    bool Skipped,
    DateTimeOffset At) : Signal;
