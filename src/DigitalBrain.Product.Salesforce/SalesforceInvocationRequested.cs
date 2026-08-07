namespace DigitalBrain.Product.Salesforce;

public sealed record SalesforceInvocationRequested : Synapse
{
    public SalesforceInvocationRequested(PreparedAccountDescriptionMutation mutation)
    {
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    public PreparedAccountDescriptionMutation Mutation { get; }
}
