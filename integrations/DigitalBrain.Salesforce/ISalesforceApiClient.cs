namespace DigitalBrain.Salesforce;

public interface ISalesforceApiClient
{
    Task<string> GetCurrentUserProfileAsync(CancellationToken ct);
    Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct);
    Task<string[]> ListContactsAsync(int maxResults, CancellationToken ct);
    Task<string> DescribeCrmAccessAsync(CancellationToken ct);
}
