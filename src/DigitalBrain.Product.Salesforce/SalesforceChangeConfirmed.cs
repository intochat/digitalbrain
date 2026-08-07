namespace DigitalBrain.Product.Salesforce;

public sealed record SalesforceChangeConfirmed : Synapse
{
    public SalesforceChangeConfirmed(PreparedAccountDescriptionMutation mutation)
    {
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    public PreparedAccountDescriptionMutation Mutation { get; }
}
