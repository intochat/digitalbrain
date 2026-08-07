namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalEvidence
{
    public ApprovalEvidence(string source, string summary, Uri? referenceUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Source = source.Trim();
        Summary = summary.Trim();
        ReferenceUri = referenceUri;
    }

    public string Source { get; }

    public string Summary { get; }

    public Uri? ReferenceUri { get; }
}
