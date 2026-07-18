using System.Globalization;
using System.Net.Mail;
using DigitalBrain.SDK.Google.Auth;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace DigitalBrain.SDK.Google.Gmail;

internal sealed class GoogleGmailService(GoogleAuthBroker broker) : IGmailService
{
    static readonly string[] Scopes = ["https://www.googleapis.com/auth/gmail.readonly"];
    static readonly string[] HeadersToFetch = ["From", "Date", "Subject"];

    public async Task<IReadOnlyList<GmailSender>> ListRecentSendersAsync(
        string userAccountId, int n, CancellationToken ct)
    {
        var credential = await broker.GetCredentialAsync(userAccountId, Scopes, ct)
            ?? throw new InvalidOperationException(
                $"No credential for '{userAccountId}'. Call Connect first.");

        var svc = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DigitalBrain",
        });

        var listReq = svc.Users.Messages.List("me");
        listReq.MaxResults = n;
        listReq.Q = "in:inbox";
        var list = await listReq.ExecuteAsync(ct);

        var messages = list.Messages ?? [];
        var results = new List<GmailSender>(messages.Count);
        foreach (var msg in messages)
        {
            var getReq = svc.Users.Messages.Get("me", msg.Id);
            getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            getReq.MetadataHeaders = HeadersToFetch;
            var full = await getReq.ExecuteAsync(ct);
            results.Add(MapToSender(full));
        }
        return results;
    }

    static GmailSender MapToSender(Message m)
    {
        var headers = m.Payload?.Headers?.ToDictionary(
            h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var from = headers.GetValueOrDefault("From", "");
        var dateRaw = headers.GetValueOrDefault("Date", "");
        var subject = headers.GetValueOrDefault("Subject", "");
        var (name, email) = ParseFromHeader(from);
        var received = TryParseRfc2822Date(dateRaw) ?? TimeProvider.System.GetUtcNow();
        return new GmailSender(name, email, received, subject);
    }

    static (string Name, string Email) ParseFromHeader(string from)
    {
        if (string.IsNullOrWhiteSpace(from)) return ("", "");
        try
        {
            var address = new MailAddress(from);
            var displayName = string.IsNullOrEmpty(address.DisplayName)
                ? LocalPartOf(address.Address)
                : address.DisplayName;
            return (displayName, address.Address);
        }
        catch (FormatException)
        {
            return (from, "");
        }
    }

    static string LocalPartOf(string email)
    {
        var at = email.IndexOf('@');
        return at < 0 ? email : email[..at];
    }

    static DateTimeOffset? TryParseRfc2822Date(string raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
}
