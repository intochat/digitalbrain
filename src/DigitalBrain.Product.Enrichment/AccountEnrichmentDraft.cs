namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// The proposed account description, before it is converted into a frozen mutation and approval.
/// </summary>
public sealed record AccountEnrichmentDraft
{
    public AccountEnrichmentDraft(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description.Trim();
    }

    public string Description { get; }
}
