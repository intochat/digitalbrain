namespace DigitalBrain.Product.Presentation;

public sealed record ApprovalInboxItemChanged : Synapse
{
    public ApprovalInboxItemChanged(
        string proposalId,
        string proposalFingerprint,
        string title,
        string summary,
        ApprovalInboxStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The inbox status is not recognized.");
        }

        ProposalId = proposalId.Trim();
        ProposalFingerprint = proposalFingerprint.Trim();
        Title = title.Trim();
        Summary = summary.Trim();
        Status = status;
    }

    public string ProposalId { get; }

    public string ProposalFingerprint { get; }

    public string Title { get; }

    public string Summary { get; }

    public ApprovalInboxStatus Status { get; }
}
