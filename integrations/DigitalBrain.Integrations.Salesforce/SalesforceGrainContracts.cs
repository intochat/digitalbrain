using Orleans;

namespace DigitalBrain.Integrations.Salesforce.Grains;

internal static class SalesforceTools
{
    public const string UpdateRecord = "salesforce.record.update";
}

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-entity")]
internal sealed record SalesforceSemanticEntity([property: Id(0)] string Label);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-field")]
internal sealed record SalesforceSemanticField([property: Id(0)] string Label);

internal enum SalesforceMutationStatus
{
    Prepared,
    Applied,
    AlreadyApplied,
    Conflict,
    VerificationFailed,
    NeedsAuth,
    ConfigurationMissing,
    AccessDenied,
    InvalidRequest,
    Unavailable
}

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-update-preview")]
internal sealed record SalesforceUpdatePreviewRequest(
    [property: Id(0)] SalesforceSemanticEntity Entity,
    [property: Id(1)] string RecordId,
    [property: Id(2)] SalesforceSemanticField Field,
    [property: Id(3)] string NewValue);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-prepared-update")]
internal sealed record SalesforcePreparedUpdate([property: Id(0)] byte[] Payload);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-mutation-preview")]
internal sealed record SalesforceMutationPreviewResult(
    [property: Id(0)] SalesforceMutationStatus Status,
    [property: Id(1)] string? OriginalValue = null,
    [property: Id(2)] SalesforcePreparedUpdate? PreparedUpdate = null,
    [property: Id(3)] string? SafeReason = null,
    [property: Id(4)] string? CanonicalDesiredValue = null,
    [property: Id(5)] string? ResolvedEntityLabel = null,
    [property: Id(6)] string? ResolvedFieldLabel = null);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-mutation-result")]
internal sealed record SalesforceMutationApplyResult([property: Id(0)] SalesforceMutationStatus Status, [property: Id(1)] string? SafeReason = null);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-verification")]
internal sealed record SalesforceMutationVerificationResult([property: Id(0)] bool Verified, [property: Id(1)] string? SafeReason = null);

[Alias("digitalbrain.v3.salesforce-mutation-grain")]
internal interface ISalesforceMutationToolGrain : IGrainWithStringKey
{
    [Alias("PreviewUpdateAsync")]
    Task<SalesforceMutationPreviewResult> PreviewUpdateAsync(SalesforceUpdatePreviewRequest request, CancellationToken cancellationToken = default);

    [Alias("ApplyUpdateAsync")]
    Task<SalesforceMutationApplyResult> ApplyUpdateAsync(SalesforcePreparedUpdate preparedUpdate, CancellationToken cancellationToken = default);

    [Alias("VerifyUpdateAsync")]
    Task<SalesforceMutationVerificationResult> VerifyUpdateAsync(SalesforcePreparedUpdate preparedUpdate, CancellationToken cancellationToken = default);
}
