namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// A reviewable, provider-neutral piece of evidence used to form a proposal.
/// </summary>
public sealed record EnrichmentEvidence
{
    public EnrichmentEvidence(string source, string summary, Uri? referenceUri = null)
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
