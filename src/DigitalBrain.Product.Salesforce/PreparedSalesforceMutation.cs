namespace DigitalBrain.Product.Salesforce;

public sealed record PreparedSalesforceMutation : Synapse
{
    public PreparedSalesforceMutation(PreparedAccountDescriptionMutation mutation)
    {
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    public PreparedAccountDescriptionMutation Mutation { get; }
}
