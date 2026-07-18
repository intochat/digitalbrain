using System.Text;

namespace DigitalBrain;

public static class ConversationKey
{
    private const string Version = "v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Encode(BrainOwnerId owner, ConversationId conversationId)
    {
        if (!IsValidOwner(owner.Value))
            throw new ArgumentException(
                "A trimmed, control-character-free authenticated owner is required.",
                nameof(owner));
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId.Value);

        return $"{Version}.{EncodeSegment(owner.Value)}.{EncodeSegment(conversationId.Value)}";
    }

    public static bool TryParse(string? key, out BrainOwnerId owner, out ConversationId conversationId)
    {
        owner = default;
        conversationId = default;

        if (string.IsNullOrEmpty(key))
            return false;

        var segments = key.Split('.');
        if (segments.Length != 3 || segments[0] != Version)
            return false;

        if (!TryDecodeSegment(segments[1], out var ownerValue) ||
            !TryDecodeSegment(segments[2], out var conversationValue))
            return false;

        if (!IsValidOwner(ownerValue))
            return false;

        try
        {
            conversationId = new ConversationId(conversationValue);
        }
        catch (ArgumentException)
        {
            return false;
        }

        owner = new BrainOwnerId(ownerValue);
        return true;
    }

    private static bool IsValidOwner(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Any(char.IsControl)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static string EncodeSegment(string value) =>
        Convert.ToBase64String(StrictUtf8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryDecodeSegment(string segment, out string value)
    {
        value = string.Empty;
        if (segment.Length == 0 || segment.Length % 4 == 1)
            return false;

        foreach (var character in segment)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
                return false;
        }

        var padded = segment
            .Replace('-', '+')
            .Replace('_', '/')
            .PadRight(segment.Length + ((4 - (segment.Length % 4)) % 4), '=');

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return false;
        }

        string candidate;
        try
        {
            candidate = StrictUtf8.GetString(decoded);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (!string.Equals(EncodeSegment(candidate), segment, StringComparison.Ordinal))
            return false;

        value = candidate;
        return true;
    }
}
