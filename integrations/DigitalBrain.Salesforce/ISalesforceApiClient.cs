using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Salesforce;

public interface ISalesforceApiClient
{
    Task<string> GetCurrentUserProfileAsync(CancellationToken ct);
    Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct);
    Task<string[]> ListContactsAsync(int maxResults, CancellationToken ct);
    Task<string> DescribeCrmAccessAsync(CancellationToken ct);

    Task<SalesforceReadPage> DiscoverObjectsAsync(SalesforceDiscoveryRequest request, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());

    Task<SalesforceReadPage> ReadRecordsAsync(SalesforceRecordReadRequest request, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());

    Task<SalesforceReadPage> SearchRecordsAsync(SalesforceSearchRequest request, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());

    Task<SalesforceReadPage> AggregateRecordsAsync(SalesforceAggregateRequest request, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());

    Task<SalesforceReadPage> ContinueRecordsAsync(SalesforceContinuation continuation, CancellationToken ct) =>
        Task.FromException<SalesforceReadPage>(SalesforceReadException.Unsupported());
}
