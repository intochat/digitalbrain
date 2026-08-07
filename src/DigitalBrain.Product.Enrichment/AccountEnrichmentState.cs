using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Salesforce;

namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Durable, product-level state for a single enrichment run.
/// </summary>
public sealed class AccountEnrichmentState
{
    public AccountEnrichmentRequest? Request { get; set; }

    public ApprovalReviewContext? ReviewContext { get; set; }

    public IReadOnlyList<EnrichmentEvidence> EmailEvidence { get; set; } = [];

    public IReadOnlyList<EnrichmentEvidence> WebEvidence { get; set; } = [];

    public bool EmailUnavailable { get; set; }

    public bool WebUnavailable { get; set; }

    public PreparedAccountDescriptionMutation? PreparedMutation { get; set; }

    public bool ProposalProposed { get; set; }

    public bool Completed { get; set; }

    public bool OutcomeUncertain { get; set; }
}
