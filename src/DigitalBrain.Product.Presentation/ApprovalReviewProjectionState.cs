namespace DigitalBrain.Product.Presentation;

public sealed class ApprovalReviewProjectionState
{
    public string? ProposalId { get; set; }

    public string? ProposalFingerprint { get; set; }

    public string? Title { get; set; }

    public string? Summary { get; set; }

    public ApprovalInboxStatus Status { get; set; }
}
