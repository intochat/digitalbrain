namespace DigitalBrain.Product.Salesforce;

/// <summary>
/// Confirms that a frozen mutation has been durably accepted before an approval may reference it.
/// </summary>
public sealed record SalesforceMutationPrepared : Synapse
{
    public SalesforceMutationPrepared(PreparedAccountDescriptionMutation mutation)
    {
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    public PreparedAccountDescriptionMutation Mutation { get; }
}
