namespace DigitalBrain.Google.Gmail;

internal static class GmailTokenPolicy
{
    internal static DateTimeOffset Expiry(int seconds, TimeProvider clock)
        => seconds is > 0 and <= 86400 ? clock.GetUtcNow().AddSeconds(seconds)
            : throw new GmailUnavailableException("Google returned an invalid token lifetime.");

    internal static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || token.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw new GmailUnavailableException("Google returned an invalid token.");
        }
    }
}
