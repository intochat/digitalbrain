namespace DigitalBrain.Product.Approvals;

/// <summary>
/// An opaque, frozen presentation destination for an approval review.
/// It is product context, never a Hosting workspace scope or executable action.
/// </summary>
public sealed record ApprovalReviewContext
{
    public ApprovalReviewContext(ApprovalReviewContextKind kind, string opaqueContextRef)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The approval review context kind is not recognized.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(opaqueContextRef);
        Kind = kind;
        OpaqueContextRef = opaqueContextRef.Trim();
    }

    public ApprovalReviewContextKind Kind { get; }

    public string OpaqueContextRef { get; }
}
