using Orleans;

namespace DigitalBrain.Kernel.V2;

public static class V2SalesforceTools
{
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
    Unavailable
}

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-read-result")]
public sealed record V2SalesforceReadResult(
    [property: Id(0)] V2SalesforceReadStatus Status,
    [property: Id(1)] string? Content = null,
    [property: Id(2)] string? SafeReason = null,
    [property: Id(3)] string? ConnectionUrl = null);

[Alias("digitalbrain.v2.salesforce-read-tool-grain")]
public interface IV2SalesforceReadToolGrain : IGrainWithStringKey
{
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
}
