namespace DigitalBrain.Product.Presentation;

public sealed record ApprovalWorkspaceSurfaceAction
{
    public ApprovalWorkspaceSurfaceAction(ApprovalReviewDecision decision, string reference)
    {
        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "The approval decision is not recognized.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        Decision = decision;
        Reference = reference.Trim();
    }

    public ApprovalReviewDecision Decision { get; }

    public string Reference { get; }
}
