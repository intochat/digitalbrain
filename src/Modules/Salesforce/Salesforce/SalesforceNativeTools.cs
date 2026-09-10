using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceNativeTools(ISalesforce salesforce, TimeProvider timeProvider)
{
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
