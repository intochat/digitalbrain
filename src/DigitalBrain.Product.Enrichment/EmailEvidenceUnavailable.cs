namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Indicates that required email evidence could not be obtained without exposing provider details.
/// </summary>
public sealed record EmailEvidenceUnavailable : Synapse
{
    public EmailEvidenceUnavailable(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        RunId = runId.Trim();
    }

    public string RunId { get; }
}
