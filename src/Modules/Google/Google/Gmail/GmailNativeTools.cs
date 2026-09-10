using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;

namespace DigitalBrain.Google;

public sealed class GmailNativeTools(IGmail gmail, BrowserLogins logins, TimeProvider timeProvider)
{
    public Task<GmailContentRead> SearchThreads(SearchGmailThreads query, CancellationToken cancellationToken = default)
        => ReadWithRefreshAsync(() => gmail.SearchThreads(query, cancellationToken), cancellationToken);

    public Task<GmailContentRead> GetThread(ReadGmailThread query, CancellationToken cancellationToken = default)
        => ReadWithRefreshAsync(() => gmail.ReadThread(query, cancellationToken), cancellationToken);

    public Task<GmailContentRead> ListLabels(CancellationToken cancellationToken = default)
        => ReadWithRefreshAsync(() => gmail.ReadLabels(cancellationToken), cancellationToken);

    private async Task<GmailContentRead> ReadWithRefreshAsync(Func<Task<GmailContentRead>> read, CancellationToken cancellationToken)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (GmailNotConnectedException)
        {
            var connection = await gmail.ReadConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!connection.Connected || connection.ExpiresAt is null || connection.ExpiresAt > timeProvider.GetUtcNow())
            {
                throw;
            }
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await gmail.Refresh(new RefreshGmailConnection(CommandId.New())).WaitAsync(timeout.Token).ConfigureAwait(false);
        while (true)
        {
            var connection = await gmail.ReadConnection().WaitAsync(timeout.Token).ConfigureAwait(false);
            if (!connection.Connected)
            {
                throw new GmailNotConnectedException();
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

    public async Task<GmailAccountStatus> GetCurrentAccount()
    {
        var connection = await gmail.ReadConnection().ConfigureAwait(false);
        return new(connection, connection.Connected ? null : logins.Require());
    }

    public Task<Accepted<GmailDraftPreview>> CreateDraft(PrepareGmailDraft command)
        => gmail.PrepareDraft(command);
}
