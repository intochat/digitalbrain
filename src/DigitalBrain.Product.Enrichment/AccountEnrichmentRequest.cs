namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// A provider-neutral request to prepare a reviewed Salesforce account-description update.
/// </summary>
public sealed record AccountEnrichmentRequest
{
    public AccountEnrichmentRequest(
        string runId,
        string accountId,
        string accountName,
        string contextId,
        string emailMessageId,
        string webQuery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emailMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(webQuery);

        RunId = runId.Trim();
        AccountId = accountId.Trim();
        AccountName = accountName.Trim();
        ContextId = contextId.Trim();
        EmailMessageId = emailMessageId.Trim();
        WebQuery = webQuery.Trim();
    }

    public string RunId { get; }

    public string AccountId { get; }

    public string AccountName { get; }

    /// <summary>
    /// Identifies the product context that initiated the run; it is never used as a hosting scope.
    /// </summary>
    public string ContextId { get; }

    public string EmailMessageId { get; }

    public string WebQuery { get; }
}
