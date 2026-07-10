using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util;

namespace DigitalBrain.Google;

public sealed class GoogleGmailApiClient : IGmailApiClient
{
    private static readonly Regex EncodedWord = new(
        @"=\?([^?\s]+)\?([bq])\?([^?]*)\?=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex RepeatedWhitespace = new(
        @"\s+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private readonly GmailService _service;

    public GoogleGmailApiClient(UserCredential credential) : this(new GmailService(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "DigitalBrain"
    }))
    {
    }

    internal GoogleGmailApiClient(GmailService service) => _service = service;

    public async Task<GmailLatestIncomingMessage> ReadLatestIncomingAsync(
        CancellationToken cancellationToken = default)
    {
        var list = _service.Users.Messages.List("me");
        list.LabelIds = new Repeatable<string>(["INBOX"]);
        list.Q = "-in:sent -in:drafts";
        list.MaxResults = 1;
        list.IncludeSpamTrash = false;
        var messages = await list.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var messageId = messages.Messages?.FirstOrDefault()?.Id;
        if (string.IsNullOrWhiteSpace(messageId))
            return new GmailLatestIncomingMessage(GmailLatestIncomingState.EmptyInbox);

        var get = _service.Users.Messages.Get("me", messageId);
        get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        get.MetadataHeaders = new Repeatable<string>(["From"]);
        var message = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var from = message.Payload?.Headers?.FirstOrDefault(static header =>
            string.Equals(header.Name, "From", StringComparison.OrdinalIgnoreCase))?.Value;
        return ParseSender(from);
    }

    internal static GmailLatestIncomingMessage ParseSender(string? from)
    {
        if (string.IsNullOrWhiteSpace(from) || from.Any(char.IsControl))
            return new GmailLatestIncomingMessage(GmailLatestIncomingState.SenderUnavailable);

        try
        {
            var addresses = new MailAddressCollection();
            addresses.Add(from);
            if (addresses.Count != 1)
                return new GmailLatestIncomingMessage(GmailLatestIncomingState.SenderUnavailable);

            var mailbox = addresses[0];
            if (string.IsNullOrWhiteSpace(mailbox.User) || string.IsNullOrWhiteSpace(mailbox.Host))
                return new GmailLatestIncomingMessage(GmailLatestIncomingState.SenderUnavailable);

            var address = mailbox.User + "@" + mailbox.Host.ToLowerInvariant();
            var displayName = DecodeDisplayName(mailbox.DisplayName);
            var sender = string.IsNullOrWhiteSpace(displayName)
                ? address
                : $"{displayName} <{address}>";
            return new GmailLatestIncomingMessage(GmailLatestIncomingState.SenderAvailable, sender, address);
        }
        catch (FormatException)
        {
            return new GmailLatestIncomingMessage(GmailLatestIncomingState.SenderUnavailable);
        }
    }

    private static string DecodeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
        var decoded = EncodedWord.Replace(displayName, static match => DecodeWord(match));
        return RepeatedWhitespace.Replace(decoded, " ").Trim();
    }

    private static string DecodeWord(Match match)
    {
        try
        {
            var encoding = Encoding.GetEncoding(match.Groups[1].Value);
            var bytes = string.Equals(match.Groups[2].Value, "b", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(match.Groups[3].Value)
                : DecodeQuotedPrintableWord(match.Groups[3].Value);
            return encoding.GetString(bytes);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return match.Value;
        }
    }

    private static byte[] DecodeQuotedPrintableWord(string value)
    {
        var bytes = new List<byte>(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '_')
            {
                bytes.Add((byte)' ');
                continue;
            }
            if (current == '=' && index + 2 < value.Length &&
                byte.TryParse(value.AsSpan(index + 1, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var decoded))
            {
                bytes.Add(decoded);
                index += 2;
                continue;
            }
            if (current > 0x7f)
                throw new FormatException("An encoded word contained a non-ASCII byte.");
            bytes.Add((byte)current);
        }
        return bytes.ToArray();
    }
}
