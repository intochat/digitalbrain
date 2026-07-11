using DigitalBrain.Kernel.V2;

namespace DigitalBrain.Salesforce;

public interface ISalesforceApiClient
{
    Task<string> GetCurrentUserProfileAsync(CancellationToken ct);
    Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct);
    Task<string[]> ListContactsAsync(int maxResults, CancellationToken ct);
    Task<string> DescribeCrmAccessAsync(CancellationToken ct);

    Task<SalesforceReadPage> DiscoverObjectsAsync(V2SalesforceDiscoveryRequest request, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());

    Task<SalesforceReadPage> ReadRecordsAsync(V2SalesforceRecordReadRequest request, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());

    Task<SalesforceReadPage> SearchRecordsAsync(V2SalesforceSearchRequest request, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());

    Task<SalesforceReadPage> AggregateRecordsAsync(V2SalesforceAggregateRequest request, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());

    Task<SalesforceReadPage> ContinueRecordsAsync(SalesforceContinuation continuation, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());
}
