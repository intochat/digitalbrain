namespace DigitalBrain.Product.Salesforce;

public sealed record SalesforceChangeOutcomeUncertain : Synapse
{
    public SalesforceChangeOutcomeUncertain(PreparedAccountDescriptionMutation mutation)
    {
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    public PreparedAccountDescriptionMutation Mutation { get; }
}
