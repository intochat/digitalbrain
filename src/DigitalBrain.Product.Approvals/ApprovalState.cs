namespace DigitalBrain.Product.Approvals;

public sealed class ApprovalState
{
    public ApprovalProposal? Proposal { get; set; }

    /// <summary>
    /// A verified decision can arrive through a separate outbox before the proposal's
    /// own delivery reaches this authority. It is applied once the proposal is frozen.
    /// </summary>
    public ApprovalDecisionRequested? BufferedDecision { get; set; }

    public ApprovalStatus Status { get; set; }

    public Guid? DecisionId { get; set; }

    public string? Actor { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}
