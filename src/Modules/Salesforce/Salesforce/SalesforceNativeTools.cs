using System.ComponentModel;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Core;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceNativeTools(ISalesforce salesforce, TimeProvider timeProvider, BrowserLogins? logins = null)
{
    internal AIFunction CreateCurrentAccount() => AIFunctionFactory.Create(GetCurrentAccount,
        "salesforce_current_account", "Check Salesforce access and return a secure sign-in card when authentication is required.");

    public async Task<bool> IsConnected() => (await salesforce.ReadConnection().ConfigureAwait(false)).Connected;

    public async Task<object> GetCurrentAccount()
    {
        try
        {
            var user = await GetUserInfo().ConfigureAwait(false);
            var connection = await salesforce.ReadConnection().ConfigureAwait(false);
            return new { kind = "connection", service = "salesforce", status = "connected", instanceUrl = connection.InstanceUrl, user = user.Content };
        }
        catch (SalesforceNotConnectedException)
        {
            return new { kind = "connection", service = "salesforce", status = "authentication_required",
                loginUrl = logins?.Require().AbsoluteUri, message = "Sign in to Salesforce, then continue your request." };
        }
    }

    internal AIFunction CreateSchema()
    {
        Task<SalesforceSchema> Invoke(
            [Description("Salesforce object API name; omit to read the index of all queryable objects.")] string? objectName = null,
            CancellationToken cancellationToken = default) => GetSchema(objectName, cancellationToken);
        return AIFunctionFactory.Create(Invoke, "salesforce_schema", "Read actual Salesforce objects, fields and relationships through its hosted MCP. Start with no objectName for the index.");
    }

    public Task<SalesforceSchema> GetSchema(string? objectName = null, CancellationToken cancellationToken = default)
        => ReadWithRefreshAsync(() => salesforce.ReadSchema(new ReadSalesforceSchema(objectName), cancellationToken), cancellationToken);

    internal AIFunction CreateGetUserInfo()
    {
        Task<SalesforceUserInfo> Invoke(
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => GetUserInfo(cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "salesforce_user_info",
            Description = "Read the connected Salesforce user information to identify the active account.",
        });
    }

    internal AIFunction CreateSoqlQuery()
    {
        Task<SalesforceQueryResult> Invoke(
            [Description("Read-only SOQL SELECT query")] string query,
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => SoqlQuery(new(query), cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "salesforce_query",
            Description = "Run a read-only SOQL query to retrieve Salesforce records.",
        });
    }

    internal AIFunction CreateRecordPreview()
    {
        Task<Accepted<SalesforceWritePreview>> Invoke(
            [Description("JSON arguments matching the Salesforce createRecord tool schema")] string arguments)
            => CreateRecord(arguments);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "prepare_salesforce_record",
            Description = "Prepare a Salesforce record creation preview for review before a separate confirmation writes it.",
        });
    }

    internal AIFunction CreateRecordUpdatePreview()
    {
        Task<Accepted<SalesforceWritePreview>> Invoke(
            [Description("JSON arguments matching the Salesforce updateRecord tool schema")] string arguments)
            => UpdateRecord(arguments);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "prepare_salesforce_record_update",
            Description = "Prepare a Salesforce record update preview for review before a separate confirmation writes it.",
        });
    }

    public Task<SalesforceUserInfo> GetUserInfo(CancellationToken cancellationToken = default)
        => ReadWithRefreshAsync(() => salesforce.ReadUserInfo(cancellationToken), cancellationToken);

    public Task<SalesforceQueryResult> SoqlQuery(SoqlQuery query, CancellationToken cancellationToken = default)
        => ReadWithRefreshAsync(() => salesforce.Query(query, cancellationToken), cancellationToken);

    private async Task<T> ReadWithRefreshAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (SalesforceNotConnectedException)
        {
            var connection = await salesforce.ReadConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!connection.Connected || connection.ExpiresAt is null || connection.ExpiresAt > timeProvider.GetUtcNow())
            {
                throw;
            }
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await salesforce.Refresh(new RefreshSalesforceConnection(CommandId.New())).WaitAsync(timeout.Token).ConfigureAwait(false);
        while (true)
        {
            var connection = await salesforce.ReadConnection().WaitAsync(timeout.Token).ConfigureAwait(false);
            if (!connection.Connected)
            {
                throw new SalesforceNotConnectedException();
            }
            if (connection.ExpiresAt > timeProvider.GetUtcNow())
            {
                break;
            }
            // This waits for refresh reaction scheduler latency, not domain time; a fake clock cannot remove it.
            await Task.Delay(20, timeout.Token).ConfigureAwait(false);
        }
        return await read().ConfigureAwait(false);
    }

    public Task<Accepted<SalesforceWritePreview>> CreateRecord(string arguments)
        => salesforce.PrepareWrite(new PrepareSalesforceWrite(CommandId.New(), "createRecord", arguments));

    public Task<Accepted<SalesforceWritePreview>> UpdateRecord(string arguments)
        => salesforce.PrepareWrite(new PrepareSalesforceWrite(CommandId.New(), "updateRecord", arguments));
}
