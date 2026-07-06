namespace DigitalBrain.Salesforce;

public interface ISalesforceApiClient
{
    Task<string[]> QueryAsync(string soql, CancellationToken ct);
    Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct);
}
