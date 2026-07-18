namespace DigitalBrain.Salesforce;

public interface ISalesforceMcpClient
{
    Task<SalesforceQueryResult> QueryRecordsAsync(string soql, CancellationToken cancellationToken = default);

    Task<SalesforceUpdateResult> UpdateRecordAsync(
        string objectType,
        string recordId,
        IReadOnlyDictionary<string, string> fields,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed record SalesforceQueryResult(int RecordCount, string Summary);

public sealed record SalesforceUpdateResult(string ProviderRecordId);
