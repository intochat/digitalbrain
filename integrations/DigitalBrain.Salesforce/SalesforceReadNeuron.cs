using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.V2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Salesforce;

[GrainType("digitalbrain.salesforce.account-read")]
public sealed class SalesforceReadNeuron(
    ILogger<SalesforceReadNeuron> logger,
    ISalesforceApiClientFactory salesforceApiClientFactory,
    IPackConfigStore store,
    [FromKeyedServices("salesforce")] IConnector connector) : Grain, IV2SalesforceReadToolGrain
{
    public Task<V2SalesforceReadResult> ReadLatestAccountAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(
            async (client, ct) =>
            {
                var accounts = await client.ListAccountsAsync(1, ct);
                return accounts.Length == 0 ? "No Salesforce accounts were found." : accounts[0];
            },
            cancellationToken);

    public Task<V2SalesforceReadResult> ReadCurrentProfileAsync(CancellationToken cancellationToken = default) =>
        ReadAsync((client, ct) => client.GetCurrentUserProfileAsync(ct), cancellationToken);

    public Task<V2SalesforceReadResult> ReadRecentAccountsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(
            async (client, ct) =>
            {
                var accounts = await client.ListAccountsAsync(10, ct);
                return accounts.Length == 0 ? "No Salesforce accounts were found." : "[" + string.Join(',', accounts) + "]";
            },
            cancellationToken);

    public Task<V2SalesforceReadResult> ReadRecentContactsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(
            async (client, ct) =>
            {
                var contacts = await client.ListContactsAsync(10, ct);
                return contacts.Length == 0 ? "No Salesforce contacts were found." : "[" + string.Join(',', contacts) + "]";
            },
            cancellationToken);

    public Task<V2SalesforceReadResult> ReadCrmSchemaAsync(CancellationToken cancellationToken = default) =>
        ReadAsync((client, ct) => client.DescribeCrmAccessAsync(ct), cancellationToken);

    private async Task<V2SalesforceReadResult> ReadAsync(
        Func<ISalesforceApiClient, CancellationToken, Task<string>> read,
        CancellationToken cancellationToken)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(cancellationToken: cancellationToken);
        if (!config.IsValid)
            return new V2SalesforceReadResult(
                V2SalesforceReadStatus.ConfigurationMissing,
                SafeReason: "Salesforce application configuration is missing.");

        var values = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!SalesforceClientFactory.HasUsableCredential(values))
            return await BuildConnectionResultAsync(owner, cancellationToken);

        try
        {
            var client = await salesforceApiClientFactory.CreateAsync(scope, cancellationToken);
            var content = await read(client, cancellationToken);
            return new V2SalesforceReadResult(
                V2SalesforceReadStatus.Success,
                content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsPermissionFailure(ex))
        {
            return await BuildConnectionResultAsync(
                owner,
                cancellationToken,
                "Salesforce authorization does not include the required read permission. Reconnect Salesforce and grant API access.");
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            return await BuildConnectionResultAsync(
                owner,
                cancellationToken,
                "Salesforce authorization expired or was revoked. Reconnect Salesforce to continue.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Principal-scoped Salesforce read failed with {ExceptionType}.", ex.GetType().Name);
            return new V2SalesforceReadResult(
                V2SalesforceReadStatus.Unavailable,
                SafeReason: "I couldn’t read Salesforce right now. Please try again later.");
        }
    }

    private async Task<V2SalesforceReadResult> BuildConnectionResultAsync(
        NeuronId owner,
        CancellationToken cancellationToken,
        string reason = "Connect your Salesforce account to let INO read Salesforce.")
    {
        var challenge = await connector.BeginAuthAsync(owner, cancellationToken: cancellationToken);
        if (challenge.IsForm || !IsAllowedSalesforceAuthorizationUrl(challenge.UrlOrForm))
            return new V2SalesforceReadResult(
                V2SalesforceReadStatus.ConfigurationMissing,
                SafeReason: "Salesforce application configuration is missing.");

        return new V2SalesforceReadResult(
            V2SalesforceReadStatus.NeedsAuth,
            SafeReason: reason,
            ConnectionUrl: challenge.UrlOrForm);
    }

    private static bool IsAllowedSalesforceAuthorizationUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(uri.Host, "login.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "test.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".my.salesforce.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsPermissionFailure(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Contains("insufficient_access", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthorizationFailure(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("reconnect", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid session", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("revoked", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }
}
