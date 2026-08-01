using System.Text;
using Google.Apis.Gmail.v1.Data;

namespace DigitalBrain.Google;

internal static class GmailMessageMapper
{
    internal static GmailMessage ToMessage(Message message, string? requestedId = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(message.Id))
        {
            throw new InvalidOperationException("Gmail get_message returned no id.");
        }

        if (!string.IsNullOrWhiteSpace(requestedId)
            && !string.Equals(requestedId, message.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Gmail get_message returned id '{message.Id}' for requested message '{requestedId}'.");
        }

        var subject = Header(message, "Subject") ?? string.Empty;
        var sender = Header(message, "From");
        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new InvalidOperationException("Gmail get_message returned no sender.");
        }

        var body = DecodePlaintextBody(message.Payload) ?? string.Empty;
        return new GmailMessage(
            message.Id,
            Bound(subject),
            sender.Trim(),
            Bound(body));
    }

    internal static GmailMessageHeader ToHeader(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.Id))
        {
            throw new InvalidOperationException("Gmail message metadata returned no id.");
        }

        var subject = Header(message, "Subject") ?? string.Empty;
        var sender = Header(message, "From");
        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new InvalidOperationException("Gmail message metadata returned no sender.");
        }

        return new GmailMessageHeader(message.Id, Bound(subject), sender.Trim());
    }

    private static string? Header(Message message, string name)
    {
        var headers = message.Payload?.Headers;
        if (headers is null)
        {
            return null;
        }

        foreach (var header in headers)
        {
            if (string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return header.Value;
            }
        }

        return null;
    }

    private static string? DecodePlaintextBody(MessagePart? part)
    {
        if (part is null)
        {
            return null;
        }

        if (string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(part.Body?.Data)
            && string.IsNullOrWhiteSpace(part.Body.AttachmentId)
            && string.IsNullOrWhiteSpace(part.Filename))
        {
            return DecodeBase64Url(part.Body.Data);
        }

        if (part.Parts is { Count: > 0 })
        {
            foreach (var child in part.Parts)
            {
                if (!string.IsNullOrWhiteSpace(child.Filename) || !string.IsNullOrWhiteSpace(child.Body?.AttachmentId))
                {
                    continue;
                }

                var nested = DecodePlaintextBody(child);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        if (string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return null;
    }

    private static string DecodeBase64Url(string data)
    {
        var padded = data.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private static string Bound(string text)
        => text.Length <= GmailPlanner.MaxBodyChars ? text : text[..GmailPlanner.MaxBodyChars];
}
