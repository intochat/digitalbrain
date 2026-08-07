namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Indicates that required web evidence could not be obtained without exposing provider details.
/// </summary>
public sealed record WebEvidenceUnavailable : Synapse
{
    public WebEvidenceUnavailable(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        RunId = runId.Trim();
    }

    public string RunId { get; }
}
