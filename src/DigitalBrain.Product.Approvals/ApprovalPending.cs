using System.Text.Json.Serialization;

namespace DigitalBrain.Product.Approvals;

/// <summary>
/// The redacted pending-approval projection available to cross-module consumers.
/// Approval keeps the executable action binding in its private durable state.
/// </summary>
public sealed record ApprovalPending : Synapse
{
    public ApprovalPending(ApprovalProposal proposal)
        : this(
            proposal?.ProposalId ?? throw new ArgumentNullException(nameof(proposal)),
            proposal.Fingerprint,
            proposal.Title,
            proposal.Summary,
            proposal.Evidence,
            proposal.Changes,
            proposal.ExpiresAt,
            proposal.ReviewContext)
    {
    }

    [JsonConstructor]
    public ApprovalPending(
        string proposalId,
        string proposalFingerprint,
        string title,
        string summary,
        IReadOnlyList<ApprovalEvidence> evidence,
        IReadOnlyList<ApprovalChange> changes,
        DateTimeOffset expiresAt,
        ApprovalReviewContext? reviewContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(changes);
        if (expiresAt == default)
        {
            throw new ArgumentException("A pending approval needs an expiry.", nameof(expiresAt));
        }

        var evidenceCopy = evidence.Select(RedactForCrossModule).ToArray();
        var changesCopy = changes.ToArray();
        if (evidenceCopy.Any(static item => item is null))
        {
            throw new ArgumentException("Approval evidence cannot contain null entries.", nameof(evidence));
        }

        if (changesCopy.Any(static item => item is null))
        {
            throw new ArgumentException("Approval changes cannot contain null entries.", nameof(changes));
        }

        ProposalId = proposalId.Trim();
        ProposalFingerprint = proposalFingerprint.Trim();
        Title = title.Trim();
        Summary = summary.Trim();
        Evidence = Array.AsReadOnly(evidenceCopy);
        Changes = Array.AsReadOnly(changesCopy);
        ExpiresAt = expiresAt;
        ReviewContext = reviewContext;
    }

    public string ProposalId { get; }

    public string ProposalFingerprint { get; }

    public string Title { get; }

    public string Summary { get; }

    public IReadOnlyList<ApprovalEvidence> Evidence { get; }

    public IReadOnlyList<ApprovalChange> Changes { get; }

    public DateTimeOffset ExpiresAt { get; }

    public ApprovalReviewContext? ReviewContext { get; }

    private static ApprovalEvidence RedactForCrossModule(ApprovalEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new ApprovalEvidence(
            evidence.Source,
            evidence.Summary,
            RedactReferenceUri(evidence.ReferenceUri));
    }

    private static Uri? RedactReferenceUri(Uri? referenceUri)
    {
        if (referenceUri is null
            || !referenceUri.IsAbsoluteUri
            || !string.Equals(referenceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(referenceUri.DnsSafeHost)
            || !string.IsNullOrEmpty(referenceUri.UserInfo)
            || referenceUri.IsLoopback
            || System.Net.IPAddress.TryParse(referenceUri.Host, out _)
            || string.Equals(referenceUri.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || referenceUri.DnsSafeHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var safe = new UriBuilder
        {
            Scheme = Uri.UriSchemeHttps,
            Host = referenceUri.DnsSafeHost,
            Port = -1,
            Path = referenceUri.AbsolutePath,
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty,
        };
        return safe.Uri;
    }
}
