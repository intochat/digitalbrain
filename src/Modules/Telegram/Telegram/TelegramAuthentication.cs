using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Telegram;

/// <summary>Provider authentication. No identity is accepted from an unsigned request field.</summary>
public static class TelegramAuthentication
{
    public static bool IsWebhookSecretValid(string configured, string? supplied) =>
        !string.IsNullOrWhiteSpace(configured) && supplied is not null &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));

    public static bool TryValidateInitData(string? authorization, string botToken, DateTimeOffset now, out long userId)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(botToken) || authorization is null || !authorization.StartsWith("tma ", StringComparison.Ordinal) || authorization.Length > 16384) { return false; }
        try
        {
            var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in authorization[4..].Split('&'))
            {
                var separator = part.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0) { return false; }
                var key = Decode(part[..separator]);
                var value = Decode(part[(separator + 1)..]);
                if (key.Contains('\n', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || !fields.TryAdd(key, value)) { return false; }
            }
            if (!fields.Remove("hash", out var hash) || hash.Length != 64 ||
                !fields.TryGetValue("auth_date", out var date) || !long.TryParse(date, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) ||
                unix > now.ToUnixTimeSeconds() + 30 || unix < now.ToUnixTimeSeconds() - 3600 ||
                !fields.TryGetValue("user", out var user)) { return false; }
            var keyBytes = HMACSHA256.HashData("WebAppData"u8, Encoding.UTF8.GetBytes(botToken));
            var check = string.Join('\n', fields.Select(pair => $"{pair.Key}={pair.Value}"));
            var expected = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(check));
            if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hash))) { return false; }
            using var document = JsonDocument.Parse(user);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() != 1) ||
                !document.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt64(out userId) || userId <= 0)
            {
                userId = 0;
                return false;
            }
            return true;
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            userId = 0;
            return false;
        }
    }

    public static CommandId CommandIdFor(string purpose, long userId, string eventId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"telegram/{purpose}/{userId.ToString(CultureInfo.InvariantCulture)}/{eventId}"));
        return new CommandId(new Guid(hash.AsSpan(0, 16)));
    }

    private static string Decode(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])))
            {
                throw new FormatException("Malformed percent escape.");
            }
        }
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}
