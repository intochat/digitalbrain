namespace DigitalBrain.Product.Salesforce;

public sealed class SalesforceMutationState
{
    public PreparedAccountDescriptionMutation? Mutation { get; set; }

    public bool InvocationRequested { get; set; }

    public SalesforceGatewayOutcome? Outcome { get; set; }
}
