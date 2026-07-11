using Orleans;
using DigitalBrain.Kernel.Abstractions;

namespace DigitalBrain.Kernel.Runtime;

public static class SalesforceTools
{
    public const string DiscoverObjects = "salesforce.read.objects";
    public const string ReadRecords = "salesforce.read.records";
    public const string SearchRecords = "salesforce.read.search";
    public const string AggregateRecords = "salesforce.read.aggregate";
    public const string ContinueRecords = "salesforce.read.continue";
    public const string PreviewMutation = "salesforce.mutation.preview";
    public const string ReadLatestAccount = "salesforce.account.read.latest";
    public const string ReadCurrentProfile = "salesforce.profile.read.current";
    public const string ReadRecentAccounts = "salesforce.accounts.read.recent";
    public const string ReadRecentContacts = "salesforce.contacts.read.recent";
    public const string ReadCrmSchema = "salesforce.crm.schema.read";
}

public enum SalesforceReadStatus
{
    Success,
    NeedsAuth,
    ConfigurationMissing,
    Unavailable,
    AccessDenied,
    LimitReached,
    InvalidRequest,
    ContinuationExpired
}

public enum SalesforceRecordReadKind
{
    List,
    Details,
    Related
}

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-semantic-entity")]
public sealed record SalesforceSemanticEntity(
    [property: Id(0)] string Label);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-semantic-field")]
public sealed record SalesforceSemanticField(
    [property: Id(0)] string Label);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-resolved-record")]
public sealed record SalesforceResolvedRecord(
    [property: Id(0)] SalesforceSemanticEntity Entity,
    [property: Id(1)] string RecordId);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-filter")]
public sealed record SalesforceFilter(
    [property: Id(0)] SalesforceSemanticField Field,
    [property: Id(1)] SemanticFilterOperator Operator,
    [property: Id(2)] string? Value = null);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-sort")]
public sealed record SalesforceSort(
    [property: Id(0)] SalesforceSemanticField Field,
    [property: Id(1)] SemanticSortDirection Direction);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-discovery-request")]
public sealed record SalesforceDiscoveryRequest(
    [property: Id(0)] int Limit = 50);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-record-read-request")]
public sealed record SalesforceRecordReadRequest(
    [property: Id(0)] SalesforceSemanticEntity Entity,
    [property: Id(1)] SalesforceRecordReadKind Kind = SalesforceRecordReadKind.List,
    [property: Id(2)] IReadOnlyList<SalesforceSemanticField>? Fields = null,
    [property: Id(3)] IReadOnlyList<SalesforceFilter>? Filters = null,
    [property: Id(4)] IReadOnlyList<SalesforceSort>? Sorts = null,
    [property: Id(5)] int Limit = 20,
    [property: Id(6)] SalesforceResolvedRecord? Record = null,
    [property: Id(7)] SalesforceResolvedRecord? RelatedTo = null,
    [property: Id(8)] SalesforceSemanticField? Relationship = null);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-search-request")]
public sealed record SalesforceSearchRequest(
    [property: Id(0)] string SearchText,
    [property: Id(1)] IReadOnlyList<SalesforceSemanticEntity>? Entities = null,
    [property: Id(2)] int Limit = 20);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-aggregate-request")]
public sealed record SalesforceAggregateRequest(
    [property: Id(0)] SalesforceSemanticEntity Entity,
    [property: Id(1)] SemanticAggregateFunction Function,
    [property: Id(2)] SalesforceSemanticField? Field = null,
    [property: Id(3)] SalesforceSemanticField? GroupBy = null,
    [property: Id(4)] IReadOnlyList<SalesforceFilter>? Filters = null,
    [property: Id(5)] int Limit = 50);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-continuation-request")]
public sealed record SalesforceContinuationRequest(
    [property: Id(0)] string Value);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-read-scope")]
public sealed record SalesforceReadScope(
    [property: Id(0)] string PrincipalId,
    [property: Id(1)] string OrganizationId,
    [property: Id(2)] string SalesforceUserId);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-continuation")]
public sealed record SalesforceContinuation(
    [property: Id(0)] string Value,
    [property: Id(1)] string PrincipalId,
    [property: Id(2)] string OrganizationId);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-read-result")]
public sealed record SalesforceReadResult(
    [property: Id(0)] SalesforceReadStatus Status,
    [property: Id(1)] string? Content = null,
    [property: Id(2)] string? SafeReason = null,
    [property: Id(3)] string? ConnectionUrl = null,
    [property: Id(4)] SalesforceReadScope? Scope = null,
    [property: Id(5)] SalesforceContinuation? Continuation = null,
    [property: Id(6)] int ReturnedCount = 0,
    [property: Id(7)] int? TotalSize = null);

[Alias("digitalbrain.v2.salesforce-read-tool-grain")]
public interface ISalesforceReadToolGrain : IGrainWithStringKey
{
    [Alias("BeginAuthorizationAsync")]
    Task<SalesforceReadResult> BeginAuthorizationAsync(
        string startToken,
        CancellationToken cancellationToken = default);

    [Alias("CompleteAuthorizationAsync")]
    Task<AuthResult> CompleteAuthorizationAsync(
        OAuthCallback callback,
        CancellationToken cancellationToken = default);

    [Alias("ReadLatestAccountAsync")]
    Task<SalesforceReadResult> ReadLatestAccountAsync(CancellationToken cancellationToken = default);

    [Alias("ReadCurrentProfileAsync")]
    Task<SalesforceReadResult> ReadCurrentProfileAsync(CancellationToken cancellationToken = default);

    [Alias("ReadRecentAccountsAsync")]
    Task<SalesforceReadResult> ReadRecentAccountsAsync(CancellationToken cancellationToken = default);

    [Alias("ReadRecentContactsAsync")]
    Task<SalesforceReadResult> ReadRecentContactsAsync(CancellationToken cancellationToken = default);

    [Alias("ReadCrmSchemaAsync")]
    Task<SalesforceReadResult> ReadCrmSchemaAsync(CancellationToken cancellationToken = default);

    [Alias("DiscoverObjectsAsync")]
    Task<SalesforceReadResult> DiscoverObjectsAsync(
        SalesforceDiscoveryRequest request,
        CancellationToken cancellationToken = default);

    [Alias("ReadRecordsAsync")]
    Task<SalesforceReadResult> ReadRecordsAsync(
        SalesforceRecordReadRequest request,
        CancellationToken cancellationToken = default);

    [Alias("SearchRecordsAsync")]
    Task<SalesforceReadResult> SearchRecordsAsync(
        SalesforceSearchRequest request,
        CancellationToken cancellationToken = default);

    [Alias("AggregateRecordsAsync")]
    Task<SalesforceReadResult> AggregateRecordsAsync(
        SalesforceAggregateRequest request,
        CancellationToken cancellationToken = default);

    [Alias("ContinueRecordsAsync")]
    Task<SalesforceReadResult> ContinueRecordsAsync(
        SalesforceContinuationRequest request,
        CancellationToken cancellationToken = default);
}
