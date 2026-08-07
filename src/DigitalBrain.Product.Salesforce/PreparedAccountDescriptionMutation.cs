namespace DigitalBrain.Product.Salesforce;

public sealed record PreparedAccountDescriptionMutation
{
    public const string ActionKind = "salesforce.account-description";

    public PreparedAccountDescriptionMutation(string mutationId, string accountId, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        MutationId = mutationId.Trim();
        AccountId = accountId.Trim();
        Description = description.Trim();
        Fingerprint = SalesforceMutationFingerprint.Compute(MutationId, AccountId, Description);
    }

    public string MutationId { get; }

    public string AccountId { get; }

    public string Description { get; }

    public string Fingerprint { get; }
}
