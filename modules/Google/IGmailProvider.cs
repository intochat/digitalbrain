using Brain.Kernel.Connections;
using Google.Contracts;
using System.Text.Json;

namespace Brain.Modules.Google;

public interface IGmailProvider
{
    Task<string> ListAsync(ConnectionToken token, int max, CancellationToken ct);
    Task<string> SendAsync(ConnectionToken token, string payloadJson, CancellationToken ct);

    async Task<GmailMailboxPage> ReadMailboxAsync(
        ConnectionToken token,
        GmailMailboxReadRequest request,
        CancellationToken ct)
    {
        var responseJson = await ListAsync(token, request.Limit, ct);
        using var response = JsonDocument.Parse(responseJson);
        var messages = response.RootElement.TryGetProperty("messages", out var entries)
            ? entries.EnumerateArray()
                .Select(entry => new GmailMessageSummary(
                    entry.GetProperty("id").GetString() ?? string.Empty,
                    entry.TryGetProperty("threadId", out var threadId) ? threadId.GetString() : null,
                    DateTimeOffset.UnixEpoch,
                    null,
                    null))
                .ToArray()
            : [];
        var continuationToken = response.RootElement.TryGetProperty("nextPageToken", out var next)
            ? next.GetString()
            : null;
        return new GmailMailboxPage(messages, continuationToken);
    }

    Task<GmailMessage> ReadMessageAsync(
        ConnectionToken token,
        GmailMessageReadRequest request,
        CancellationToken ct) =>
        throw new NotSupportedException("This Gmail provider does not support reading individual messages.");

    Task<string> SendAsync(
        ConnectionToken token,
        GmailSendProposal proposal,
        CancellationToken ct) =>
        SendAsync(
            token,
            JsonSerializer.Serialize(new
            {
                to = proposal.Recipient,
                subject = proposal.Subject,
                body = proposal.Body
            }, JsonSerializerOptions.Web),
            ct);
}
