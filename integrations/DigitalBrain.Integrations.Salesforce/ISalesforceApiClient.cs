using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Runtime;
namespace DigitalBrain.Integrations.Salesforce;

internal interface ISalesforceApiClient
{
    Task<SalesforceRecord> ReadRecordAsync(DigitalBrain.Integrations.Salesforce.Contracts.SalesforceRecordReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<SalesforceRecord>(new NotSupportedException());
    Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct);
    Task<SalesforceMutationPreviewResult> PreviewUpdateAsync(SalesforceUpdatePreviewRequest request, CancellationToken ct) =>
        Task.FromResult(new SalesforceMutationPreviewResult(SalesforceMutationStatus.Unavailable, SafeReason: "Salesforce updates are unavailable right now."));
    Task<SalesforceMutationApplyResult> ApplyUpdateAsync(SalesforcePreparedUpdate preparedUpdate, CancellationToken ct) =>
        Task.FromResult(new SalesforceMutationApplyResult(SalesforceMutationStatus.Unavailable, "Salesforce updates are unavailable right now."));
    Task<SalesforceMutationVerificationResult> VerifyUpdateAsync(SalesforcePreparedUpdate preparedUpdate, CancellationToken ct) =>
        Task.FromResult(new SalesforceMutationVerificationResult(false, "Salesforce updates are unavailable right now."));
}
