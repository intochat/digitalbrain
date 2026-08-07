namespace DigitalBrain.Product.Enrichment;

public sealed record EmailEvidenceCollected : Synapse
{
    public EmailEvidenceCollected(string runId, IReadOnlyList<EnrichmentEvidence> evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(evidence);

        var copy = evidence.ToArray();
        if (copy.Any(static item => item is null))
        {
            throw new ArgumentException("Collected evidence cannot contain null entries.", nameof(evidence));
        }

        RunId = runId.Trim();
        Evidence = Array.AsReadOnly(copy);
    }

    public string RunId { get; }

    public IReadOnlyList<EnrichmentEvidence> Evidence { get; }
}
