namespace DigitalBrain.Product.Salesforce;

public sealed class SalesforceMutationState
{
    public PreparedAccountDescriptionMutation? Mutation { get; set; }

    public bool InvocationRequested { get; set; }

    public string? ApprovedProposalId { get; set; }

    public string? ApprovedProposalFingerprint { get; set; }

    public SalesforceGatewayOutcome? Outcome { get; set; }
}
