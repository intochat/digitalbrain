using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Kernel.Runtime;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util;

namespace DigitalBrain.Google;

public sealed class GoogleGmailApiClient : IGmailApiClient
{
    internal const int CandidateWindowSize = 16;
    private const string AttachmentLimitation =
        "Attachment metadata requires a separately authorized Gmail capability.";
    private static readonly string[] MetadataHeaderNames = ["From", "To", "Subject"];
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

    public async Task<GmailSendResult> SendAsync(
        GmailSendRequest request,
        CancellationToken cancellationToken = default)
    {
        GmailSendRequestValidator.Validate(request);

        var messageId = GmailSendRequestValidator.MessageId(request.UniqueTag);
        var existingRequest = _service.Users.Messages.List("me");
        existingRequest.LabelIds = new Repeatable<string>(["SENT"]);
        existingRequest.Q = $"in:sent rfc822msgid:{messageId}";
        existingRequest.MaxResults = 1;
        existingRequest.Fields = "messages(id,threadId)";
        var existing = await existingRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var duplicate = existing.Messages?.FirstOrDefault();
        if (duplicate is not null)
            return new GmailSendResult(
                GmailSendStatus.AlreadyApplied,
                duplicate.Id,
                duplicate.ThreadId);

        var send = _service.Users.Messages.Send(new Message
        {
            Raw = Base64Url(Rfc2822(request, messageId))
        }, "me");
        send.Fields = "id,threadId";
        var sent = await send.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return new GmailSendResult(GmailSendStatus.Applied, sent.Id, sent.ThreadId);
    }

    public async Task<GmailLatestIncomingMessage> ReadIncomingAtOffsetAsync(
        GmailIncomingReadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Offset is < 0 or > GmailTools.MaximumOffset)
            throw new ArgumentOutOfRangeException(nameof(request));
        if ((request.AnchorMessageId is null) != (request.AnchorInternalDate is null))
            throw new ArgumentException("A complete Gmail anchor is required.", nameof(request));

        var window = await ReadMetadataWindowAsync(new GmailMessageSelection(
            Mailbox: GmailMailboxScope.Inbox,
            MaxPages: 1,
            MaxCandidates: CandidateWindowSize), cancellationToken).ConfigureAwait(false);
        var ordered = window.Messages;
        if (ordered.Length == 0)
            return new GmailLatestIncomingMessage(request.Offset == 0 && request.AnchorMessageId is null
                ? GmailLatestIncomingState.EmptyInbox
                : GmailLatestIncomingState.PositionUnavailable);

        var start = 0;
        if (request.AnchorMessageId is not null)
        {
            var anchorIndex = Array.FindIndex(ordered, candidate =>
                string.Equals(candidate.MessageId, request.AnchorMessageId, StringComparison.Ordinal) &&
                candidate.InternalDate == request.AnchorInternalDate);
            if (anchorIndex < 0)
                return new GmailLatestIncomingMessage(GmailLatestIncomingState.PositionUnavailable);
            start = anchorIndex;
        }

        var requestedIndex = start + request.Offset;
        if (requestedIndex >= ordered.Length)
            return new GmailLatestIncomingMessage(GmailLatestIncomingState.PositionUnavailable);
        var requested = ordered[requestedIndex];
        return (requested.FromAddress is null
            ? new GmailLatestIncomingMessage(GmailLatestIncomingState.SenderUnavailable)
            : new GmailLatestIncomingMessage(
                GmailLatestIncomingState.SenderAvailable,
                requested.From,
                requested.FromAddress)) with
        {
            MessageId = requested.MessageId,
            InternalDate = requested.InternalDate
        };
    }

    public async Task<GmailMessageListResult> ListMessagesAsync(
        GmailMessageListRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request.Selection, request.Offset, request.Limit);
        if (request.Selection.AttachmentFilter != GmailAttachmentFilter.Any)
            return new GmailMessageListResult(
                GmailMetadataReadState.CapabilityUnavailable,
                [],
                EmptyCoverage(),
                AttachmentLimitation);

        var stableCandidateIds = request.Selection.PinnedMessageIds?
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var pageSelection = request.Selection;
        var pageOffset = request.Offset;
        if (stableCandidateIds is not null)
        {
            if (request.Offset >= stableCandidateIds.Length)
                throw new ArgumentOutOfRangeException(nameof(request));
            pageSelection = request.Selection with
            {
                PinnedMessageIds = stableCandidateIds
                    .Skip(request.Offset)
                    .Take(request.Limit)
                    .ToArray()
            };
            pageOffset = 0;
        }

        var window = await ReadMetadataWindowAsync(pageSelection, cancellationToken).ConfigureAwait(false);
        var messages = window.Messages.Skip(pageOffset).Take(request.Limit).ToArray();
        if (stableCandidateIds is not null)
        {
            var messagesById = messages.ToDictionary(static message => message.MessageId, StringComparer.Ordinal);
            messages = pageSelection.PinnedMessageIds!
                .Where(messagesById.ContainsKey)
                .Select(id => messagesById[id])
                .ToArray();
        }
        return new GmailMessageListResult(
            GmailMetadataReadState.Success,
            messages,
            window.Coverage,
            StableCandidateMessageIds: stableCandidateIds ??
                window.Messages.Select(static message => message.MessageId).ToArray());
    }

    public async Task<GmailMailboxOverview> ReadMailboxOverviewAsync(CancellationToken cancellationToken = default)
    {
        var get = _service.Users.Labels.Get("me", "INBOX");
        get.Fields = "id,messagesTotal,messagesUnread,threadsTotal,threadsUnread";
        var label = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return new GmailMailboxOverview(
            label.MessagesTotal ?? 0,
            label.MessagesUnread ?? 0,
            label.ThreadsTotal ?? 0,
            label.ThreadsUnread ?? 0);
    }

    public async Task<GmailThreadListResult> ListThreadsAsync(
        GmailThreadListRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request.Selection, request.Offset, request.Limit);
        if (request.MaxMessagesPerThread is < 1 or > GmailTools.MaximumResultCount)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Selection.AttachmentFilter != GmailAttachmentFilter.Any)
            return new GmailThreadListResult(
                GmailMetadataReadState.CapabilityUnavailable,
                [],
                EmptyCoverage(),
                AttachmentLimitation);

        var window = await ReadMetadataWindowAsync(request.Selection, cancellationToken).ConfigureAwait(false);
        var stableThreads = window.Messages
            .Where(static message => !string.IsNullOrWhiteSpace(message.ThreadId))
            .GroupBy(static message => message.ThreadId!, StringComparer.Ordinal)
            .Select(group =>
            {
                var messages = group
                    .OrderByDescending(static message => message.InternalDate)
                    .ThenBy(static message => message.MessageId, StringComparer.Ordinal)
                    .ToArray();
                var participants = messages
                    .SelectMany(static message => message.FromAddress is null
                        ? message.ToAddresses
                        : message.ToAddresses.Prepend(message.FromAddress))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToArray();
                return new GmailThreadMetadata(
                    group.Key,
                    messages[0].InternalDate,
                    messages.Select(static message => message.Subject).FirstOrDefault(static subject => subject is not null),
                    participants,
                    messages.Any(static message => !message.IsRead),
                    messages.Length,
                    messages.Take(request.MaxMessagesPerThread).ToArray());
            })
            .OrderByDescending(static thread => thread.LatestInternalDate)
            .ThenBy(static thread => thread.ThreadId, StringComparer.Ordinal)
            .ToArray();
        var threads = stableThreads
            .Skip(request.Offset)
            .Take(request.Limit)
            .ToArray();
        return new GmailThreadListResult(
            GmailMetadataReadState.Success,
            threads,
            window.Coverage,
            StableCandidateMessageIds: window.Messages.Select(static message => message.MessageId).ToArray(),
            StableCandidateThreadIds: stableThreads.Select(static thread => thread.ThreadId).ToArray());
    }

    private async Task<GmailMetadataWindow> ReadMetadataWindowAsync(
        GmailMessageSelection selection,
        CancellationToken cancellationToken)
    {
        Validate(selection, 0, 1);
        var ids = new List<string>(selection.MaxCandidates);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pagesRead = 0;
        var providerExhausted = selection.PinnedMessageIds is not null;
        var candidateLimitReached = false;

        if (selection.PinnedMessageIds is not null)
        {
            foreach (var id in selection.PinnedMessageIds)
                if (seen.Add(id)) ids.Add(id);
        }
        else
        {
            string? pageToken = null;
            while (pagesRead < selection.MaxPages && ids.Count < selection.MaxCandidates)
            {
                var list = _service.Users.Messages.List("me");
                var labelId = ProviderLabel(selection.Mailbox);
                if (labelId is not null) list.LabelIds = new Repeatable<string>([labelId]);
                list.MaxResults = selection.MaxCandidates - ids.Count;
                list.PageToken = pageToken;
                list.IncludeSpamTrash = false;
                list.Fields = "messages(id,threadId),nextPageToken";
                var page = await list.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                pagesRead++;
                foreach (var item in page.Messages ?? [])
                {
                    if (string.IsNullOrWhiteSpace(item.Id) || !seen.Add(item.Id)) continue;
                    ids.Add(item.Id);
                    if (ids.Count == selection.MaxCandidates) break;
                }

                if (string.IsNullOrWhiteSpace(page.NextPageToken))
                {
                    providerExhausted = true;
                    break;
                }
                if (string.Equals(pageToken, page.NextPageToken, StringComparison.Ordinal)) break;
                pageToken = page.NextPageToken;
            }
            candidateLimitReached = !providerExhausted && ids.Count == selection.MaxCandidates;
        }

        var metadata = new List<GmailMessageMetadata>(ids.Count);
        var unavailable = 0;
        foreach (var id in ids)
        {
            try
            {
                var get = _service.Users.Messages.Get("me", id);
                get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                get.MetadataHeaders = new Repeatable<string>(MetadataHeaderNames);
                get.Fields = "id,threadId,internalDate,labelIds,payload(headers)";
                var message = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (message.InternalDate is not long internalDate)
                {
                    unavailable++;
                    continue;
                }
                metadata.Add(ToMetadata(id, internalDate, message));
            }
            catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
            {
                unavailable++;
            }
        }

        var matches = metadata
            .Where(message => Matches(selection, message))
            .OrderByDescending(static message => message.InternalDate)
            .ThenBy(static message => message.MessageId, StringComparer.Ordinal)
            .ToArray();
        return new GmailMetadataWindow(matches, new GmailResultCoverage(
            pagesRead,
            ids.Count,
            metadata.Count,
            matches.Length,
            unavailable,
            providerExhausted,
            candidateLimitReached));
    }

    private static GmailMessageMetadata ToMetadata(string requestedId, long internalDate, Message message)
    {
        var fromHeader = SingleHeader(message, "From");
        var sender = ParseSender(fromHeader);
        var toHeader = JoinedHeader(message, "To");
        var labels = message.LabelIds?.Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).ToArray() ?? [];
        return new GmailMessageMetadata(
            string.IsNullOrWhiteSpace(message.Id) ? requestedId : message.Id,
            message.ThreadId,
            internalDate,
            sender.State == GmailLatestIncomingState.SenderAvailable ? sender.Sender : null,
            sender.SenderAddress,
            toHeader,
            ParseAddresses(toHeader),
            SingleHeader(message, "Subject"),
            labels,
            !labels.Contains("UNREAD", StringComparer.Ordinal));
    }

    private static bool Matches(GmailMessageSelection selection, GmailMessageMetadata message)
    {
        var labels = message.LabelIds;
        var mailboxMatch = selection.Mailbox switch
        {
            GmailMailboxScope.Incoming => !HasAny(labels, "SENT", "DRAFT", "SPAM", "TRASH"),
            GmailMailboxScope.Inbox => labels.Contains("INBOX", StringComparer.Ordinal) &&
                                       !HasAny(labels, "SENT", "DRAFT", "SPAM", "TRASH"),
            GmailMailboxScope.Sent => labels.Contains("SENT", StringComparer.Ordinal),
            GmailMailboxScope.Drafts => labels.Contains("DRAFT", StringComparer.Ordinal),
            _ => !HasAny(labels, "SPAM", "TRASH")
        };
        return mailboxMatch &&
               (selection.ReadState == GmailMessageReadState.Any ||
                selection.ReadState == GmailMessageReadState.Read && message.IsRead ||
                selection.ReadState == GmailMessageReadState.Unread && !message.IsRead) &&
               (selection.SenderAddress is null || string.Equals(
                   selection.SenderAddress, message.FromAddress, StringComparison.OrdinalIgnoreCase)) &&
               (selection.RecipientAddress is null || message.ToAddresses.Contains(
                   selection.RecipientAddress, StringComparer.OrdinalIgnoreCase)) &&
               (selection.SubjectContains is null || message.Subject?.Contains(
                   selection.SubjectContains, StringComparison.OrdinalIgnoreCase) == true) &&
               (selection.ReceivedAfterInclusive is null || message.InternalDate >= selection.ReceivedAfterInclusive) &&
               (selection.ReceivedBeforeExclusive is null || message.InternalDate < selection.ReceivedBeforeExclusive);
    }

    private static void Validate(GmailMessageSelection selection, int offset, int limit)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (offset is < 0 or >= GmailTools.MaximumCandidateCount) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > GmailTools.MaximumResultCount) throw new ArgumentOutOfRangeException(nameof(limit));
        if (selection.MaxPages is < 1 or > GmailTools.MaximumPageCount) throw new ArgumentOutOfRangeException(nameof(selection));
        if (selection.MaxCandidates is < 1 or > GmailTools.MaximumCandidateCount) throw new ArgumentOutOfRangeException(nameof(selection));
        if (selection.PinnedMessageIds is { Length: > 0 } pinned &&
            (pinned.Length > selection.MaxCandidates || pinned.Any(static id =>
                string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl))))
            throw new ArgumentException("Pinned Gmail message ids are invalid.", nameof(selection));
        if (selection.PinnedMessageIds is { Length: 0 }) throw new ArgumentException("Pinned Gmail ids cannot be empty.", nameof(selection));
        if (selection.SenderAddress is { Length: > 320 } || selection.RecipientAddress is { Length: > 320 } ||
            selection.SubjectContains is { Length: > 256 })
            throw new ArgumentException("Gmail filters exceed their bounds.", nameof(selection));
        if (selection.ReceivedAfterInclusive is < 0 || selection.ReceivedBeforeExclusive is < 0 ||
            selection.ReceivedAfterInclusive >= selection.ReceivedBeforeExclusive)
            throw new ArgumentException("The Gmail date range is invalid.", nameof(selection));
        if (selection.SenderAddress is not null && ParseSender(selection.SenderAddress).SenderAddress is null)
            throw new ArgumentException("The Gmail sender filter is invalid.", nameof(selection));
        if (selection.RecipientAddress is not null && ParseAddresses(selection.RecipientAddress).Length != 1)
            throw new ArgumentException("The Gmail recipient filter is invalid.", nameof(selection));
    }

    private static byte[] Rfc2822(GmailSendRequest request, string messageId)
    {
        var normalizedBody = request.Body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal);
        var encodedSubject = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Subject));
        var encodedBody = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(normalizedBody),
            Base64FormattingOptions.InsertLineBreaks);
        var message = string.Join("\r\n",
            $"To: {request.Recipient.Trim()}",
            $"Subject: =?UTF-8?B?{encodedSubject}?=",
            $"Message-ID: <{messageId}>",
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=utf-8",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            encodedBody);
        return Encoding.UTF8.GetBytes(message);
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? ProviderLabel(GmailMailboxScope mailbox) => mailbox switch
    {
        GmailMailboxScope.Inbox => "INBOX",
        GmailMailboxScope.Sent => "SENT",
        GmailMailboxScope.Drafts => "DRAFT",
        _ => null
    };

    private static bool HasAny(string[] labels, params string[] expected) =>
        expected.Any(label => labels.Contains(label, StringComparer.Ordinal));

    private static string? SingleHeader(Message message, string name)
    {
        var values = HeaderValues(message, name);
        return values.Length == 1 ? values[0] : null;
    }

    private static string? JoinedHeader(Message message, string name)
    {
        var values = HeaderValues(message, name);
        return values.Length == 0 ? null : string.Join(", ", values);
    }

    private static string[] HeaderValues(Message message, string name) =>
        message.Payload?.Headers?
            .Where(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(static header => NormalizeHeader(header.Value))
            .Where(static value => value is not null)
            .Select(static value => value!)
            .Take(8)
            .ToArray() ?? [];

    private static string? NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Length > 4096) return null;
        return RepeatedWhitespace.Replace(value, " ").Trim();
    }

    private static string[] ParseAddresses(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) return [];
        try
        {
            var addresses = new MailAddressCollection();
            addresses.Add(value);
            return addresses.Cast<MailAddress>()
                .Where(static address => !string.IsNullOrWhiteSpace(address.User) && !string.IsNullOrWhiteSpace(address.Host))
                .Select(static address => address.User + "@" + address.Host.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToArray();
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static GmailResultCoverage EmptyCoverage() => new(0, 0, 0, 0, 0, true, false);

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

    private sealed record GmailMetadataWindow(GmailMessageMetadata[] Messages, GmailResultCoverage Coverage);
}

internal static class GmailSendRequestValidator
{
    private const string MessageIdDomain = "digitalbrain.invalid";

    public static void Validate(GmailSendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var recipient = request.Recipient?.Trim();
        if (string.IsNullOrWhiteSpace(recipient) ||
            recipient.Length > GmailTools.MaximumRecipientLength ||
            recipient.Any(char.IsControl) ||
            !MailAddress.TryCreate(recipient, out var parsed) ||
            !string.Equals(parsed.Address, recipient, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Gmail recipient is invalid.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Subject) ||
            request.Subject.Length > GmailTools.MaximumSubjectLength ||
            request.Subject.Any(char.IsControl))
            throw new ArgumentException("The Gmail subject is invalid.", nameof(request));

        if (string.IsNullOrEmpty(request.Body) ||
            request.Body.Length > GmailTools.MaximumBodyLength ||
            request.Body.Any(static value => char.IsControl(value) && value is not '\r' and not '\n' and not '\t'))
            throw new ArgumentException("The Gmail body is invalid.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.UniqueTag) ||
            request.UniqueTag.Length > GmailTools.MaximumUniqueTagLength ||
            !IsAsciiLetterOrDigit(request.UniqueTag[0]) ||
            !IsAsciiLetterOrDigit(request.UniqueTag[^1]) ||
            request.UniqueTag.Contains("..", StringComparison.Ordinal) ||
            request.UniqueTag.Any(static value =>
                !IsAsciiLetterOrDigit(value) && value is not '-' and not '_' and not '.'))
            throw new ArgumentException("The Gmail unique tag is invalid.", nameof(request));
    }

    public static bool IsValid(GmailSendRequest request)
    {
        try
        {
            Validate(request);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string MessageId(string uniqueTag) => $"{uniqueTag}@{MessageIdDomain}";

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');
}
