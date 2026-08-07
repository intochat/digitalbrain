namespace DigitalBrain.Product.Approvals;

/// <summary>
/// A public lifecycle projection emitted only by the approval state machine.
/// It deliberately carries no action binding or execution target.
/// </summary>
public sealed record ApprovalStatusChanged : Synapse
{
    public ApprovalStatusChanged(
        string proposalId,
        string proposalFingerprint,
        ApprovalStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalFingerprint);
        if (!Enum.IsDefined(status) || status == ApprovalStatus.None)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The approval status is not recognized.");
        }

        ProposalId = proposalId.Trim();
        ProposalFingerprint = proposalFingerprint.Trim();
        Status = status;
    }

    public string ProposalId { get; }

    public string ProposalFingerprint { get; }

    public ApprovalStatus Status { get; }
}
