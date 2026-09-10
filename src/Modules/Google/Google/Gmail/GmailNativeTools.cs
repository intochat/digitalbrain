using System.ComponentModel;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Google;

public sealed class GmailNativeTools(IGmail gmail, BrowserLogins logins, TimeProvider timeProvider)
{
    internal AIFunction CreateSearchThreads()
    {
        Task<GmailContentRead> Invoke(
            [Description("Gmail search query")] string query,
            [Description("Maximum threads to return, from 1 to 10")] int pageSize = 10,
            [Description("Page token from a previous search")] string? pageToken = null,
            [Description("Cancels the operation")] CancellationToken cancellationToken = default)
            => SearchThreads(new(query, pageSize, pageToken), cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "search_gmail_threads",
            Description = "Search Gmail threads using Gmail search syntax to find relevant messages.",
        });
    }

    internal AIFunction CreateGetThread()
    {
        Task<GmailContentRead> Invoke(
            [Description("Gmail thread identifier")] string threadId,
            [Description("Message format: MINIMAL or PLAIN_TEXT")] string messageFormat = "MINIMAL",
            [Description("Cancels the operation")] CancellationToken cancellationToken = default)
            => GetThread(new(threadId, messageFormat), cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "read_gmail_thread",
            Description = "Read a Gmail thread to inspect its messages.",
        });
    }

    internal AIFunction CreateListLabels()
    {
        Task<GmailContentRead> Invoke(
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => ListLabels(cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "list_gmail_labels",
            Description = "List Gmail labels to identify folders and organize searches.",
        });
    }

    internal AIFunction CreateGetCurrentAccount()
    {
        Task<GmailAccountStatus> Invoke()
            => GetCurrentAccount();

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "gmail_current_account",
            Description = "Read the current Gmail connection and obtain a sign-in link when needed.",
        });
    }

    internal AIFunction CreateDraftPreview()
    {
        Task<Accepted<GmailDraftPreview>> Invoke(
            [Description("Recipient email addresses")] string[] to,
            [Description("Cc email addresses, or an empty array")] string[] cc,
            [Description("Bcc email addresses, or an empty array")] string[] bcc,
            [Description("Draft subject")] string subject,
            [Description("Draft plain-text body")] string body)
            => CreateDraft(new(CommandId.New(), to, cc, bcc, subject, body));

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "prepare_gmail_draft",
            Description = "Prepare a Gmail draft preview for review before a separate confirmation creates the draft.",
        });
    }

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
