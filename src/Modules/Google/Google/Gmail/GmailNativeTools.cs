using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Core;

namespace DigitalBrain.Google;

public sealed record GmailAccountStatus(GmailConnection Connection, Uri? LoginUrl);

public sealed class GmailNativeTools(IGmail gmail, BrowserLogins logins)
{
    public Task<GmailContentRead> SearchThreads(SearchGmailThreads query, CancellationToken cancellationToken = default)
        => gmail.SearchThreads(query, cancellationToken);

    public Task<GmailContentRead> GetThread(ReadGmailThread query, CancellationToken cancellationToken = default)
        => gmail.ReadThread(query, cancellationToken);

    public Task<GmailContentRead> ListLabels(CancellationToken cancellationToken = default)
        => gmail.ReadLabels(cancellationToken);

    public async Task<GmailAccountStatus> GetCurrentAccount()
    {
        var connection = await gmail.ReadConnection().ConfigureAwait(false);
        return new(connection, connection.Connected ? null : logins.Require());
    }

    public Task<Accepted<GmailDraftPreview>> CreateDraft(PrepareGmailDraft command)
        => gmail.PrepareDraft(command);
}
