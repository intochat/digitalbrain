using Orleans;
using DigitalBrain.Kernel.Abstractions;

namespace DigitalBrain.Kernel.V2;

public static class V2SalesforceTools
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

public enum V2SalesforceReadStatus
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

public enum V2SalesforceRecordReadKind
{
    List,
    Details,
    Related
}

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-semantic-entity")]
public sealed record V2SalesforceSemanticEntity(
    [property: Id(0)] string Label);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-semantic-field")]
public sealed record V2SalesforceSemanticField(
    [property: Id(0)] string Label);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-resolved-record")]
public sealed record V2SalesforceResolvedRecord(
    [property: Id(0)] V2SalesforceSemanticEntity Entity,
    [property: Id(1)] string RecordId);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-filter")]
public sealed record V2SalesforceFilter(
    [property: Id(0)] V2SalesforceSemanticField Field,
    [property: Id(1)] V2SemanticFilterOperator Operator,
    [property: Id(2)] string? Value = null);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-sort")]
public sealed record V2SalesforceSort(
    [property: Id(0)] V2SalesforceSemanticField Field,
    [property: Id(1)] V2SemanticSortDirection Direction);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-discovery-request")]
public sealed record V2SalesforceDiscoveryRequest(
    [property: Id(0)] int Limit = 50);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-record-read-request")]
public sealed record V2SalesforceRecordReadRequest(
    [property: Id(0)] V2SalesforceSemanticEntity Entity,
    [property: Id(1)] V2SalesforceRecordReadKind Kind = V2SalesforceRecordReadKind.List,
    [property: Id(2)] IReadOnlyList<V2SalesforceSemanticField>? Fields = null,
    [property: Id(3)] IReadOnlyList<V2SalesforceFilter>? Filters = null,
    [property: Id(4)] IReadOnlyList<V2SalesforceSort>? Sorts = null,
    [property: Id(5)] int Limit = 20,
    [property: Id(6)] V2SalesforceResolvedRecord? Record = null,
    [property: Id(7)] V2SalesforceResolvedRecord? RelatedTo = null,
    [property: Id(8)] V2SalesforceSemanticField? Relationship = null);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-search-request")]
public sealed record V2SalesforceSearchRequest(
    [property: Id(0)] string SearchText,
    [property: Id(1)] IReadOnlyList<V2SalesforceSemanticEntity>? Entities = null,
    [property: Id(2)] int Limit = 20);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-aggregate-request")]
public sealed record V2SalesforceAggregateRequest(
    [property: Id(0)] V2SalesforceSemanticEntity Entity,
    [property: Id(1)] V2SemanticAggregateFunction Function,
    [property: Id(2)] V2SalesforceSemanticField? Field = null,
    [property: Id(3)] V2SalesforceSemanticField? GroupBy = null,
    [property: Id(4)] IReadOnlyList<V2SalesforceFilter>? Filters = null,
    [property: Id(5)] int Limit = 50);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-continuation-request")]
public sealed record V2SalesforceContinuationRequest(
    [property: Id(0)] string Value);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-read-scope")]
public sealed record V2SalesforceReadScope(
    [property: Id(0)] string PrincipalId,
    [property: Id(1)] string OrganizationId,
    [property: Id(2)] string SalesforceUserId);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-continuation")]
public sealed record V2SalesforceContinuation(
    [property: Id(0)] string Value,
    [property: Id(1)] string PrincipalId,
    [property: Id(2)] string OrganizationId);

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-read-result")]
public sealed record V2SalesforceReadResult(
    [property: Id(0)] V2SalesforceReadStatus Status,
    [property: Id(1)] string? Content = null,
    [property: Id(2)] string? SafeReason = null,
    [property: Id(3)] string? ConnectionUrl = null,
    [property: Id(4)] V2SalesforceReadScope? Scope = null,
    [property: Id(5)] V2SalesforceContinuation? Continuation = null,
    [property: Id(6)] int ReturnedCount = 0,
    [property: Id(7)] int? TotalSize = null);

[Alias("digitalbrain.v2.salesforce-read-tool-grain")]
public interface IV2SalesforceReadToolGrain : IGrainWithStringKey
{
    [Alias("BeginAuthorizationAsync")]
    Task<V2SalesforceReadResult> BeginAuthorizationAsync(
        string startToken,
        CancellationToken cancellationToken = default);

    [Alias("CompleteAuthorizationAsync")]
    Task<AuthResult> CompleteAuthorizationAsync(
        OAuthCallback callback,
        CancellationToken cancellationToken = default);

    [Alias("ReadLatestAccountAsync")]
    Task<V2SalesforceReadResult> ReadLatestAccountAsync(CancellationToken cancellationToken = default);

    [Alias("ReadCurrentProfileAsync")]
    Task<V2SalesforceReadResult> ReadCurrentProfileAsync(CancellationToken cancellationToken = default);

    [Alias("ReadRecentAccountsAsync")]
    Task<V2SalesforceReadResult> ReadRecentAccountsAsync(CancellationToken cancellationToken = default);

    [Alias("ReadRecentContactsAsync")]
    Task<V2SalesforceReadResult> ReadRecentContactsAsync(CancellationToken cancellationToken = default);

    [Alias("ReadCrmSchemaAsync")]
    Task<V2SalesforceReadResult> ReadCrmSchemaAsync(CancellationToken cancellationToken = default);

    [Alias("DiscoverObjectsAsync")]
    Task<V2SalesforceReadResult> DiscoverObjectsAsync(
        V2SalesforceDiscoveryRequest request,
        CancellationToken cancellationToken = default);

    [Alias("ReadRecordsAsync")]
    Task<V2SalesforceReadResult> ReadRecordsAsync(
        V2SalesforceRecordReadRequest request,
        CancellationToken cancellationToken = default);

    [Alias("SearchRecordsAsync")]
    Task<V2SalesforceReadResult> SearchRecordsAsync(
        V2SalesforceSearchRequest request,
        CancellationToken cancellationToken = default);

    [Alias("AggregateRecordsAsync")]
    Task<V2SalesforceReadResult> AggregateRecordsAsync(
        V2SalesforceAggregateRequest request,
        CancellationToken cancellationToken = default);

    [Alias("ContinueRecordsAsync")]
    Task<V2SalesforceReadResult> ContinueRecordsAsync(
        V2SalesforceContinuationRequest request,
        CancellationToken cancellationToken = default);
}
