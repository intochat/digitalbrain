using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Google.Gmail;

internal sealed record GmailPubSubPush(GmailPubSubMessage? Message, string? Subscription);

internal sealed record GmailPubSubMessage(string? Data, string? MessageId);

internal static class GmailPubSub
{
    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryUnwrap(GmailPubSubPush envelope, [NotNullWhen(true)] out GmailWatchPush? push)
    {
        push = null;
        var data = envelope.Message?.Data;
        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(PadBase64(data.Replace('-', '+').Replace('_', '/')));
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            push = JsonSerializer.Deserialize<GmailWatchPush>(Encoding.UTF8.GetString(bytes), PayloadJson);
        }
        catch (JsonException)
        {
            return false;
        }

        return push is not null
            && !string.IsNullOrWhiteSpace(push.EmailAddress)
            && !string.IsNullOrWhiteSpace(push.HistoryId);
    }

    private static string PadBase64(string value) => (value.Length % 4) switch
    {
        2 => value + "==",
        3 => value + "=",
        _ => value,
    };
}