namespace DigitalBrain.Core.Runtime;

public static class GrpcAuthentication
{
    public static bool TryAuthenticate(IReadOnlyDictionary<string, string> metadata, SessionTokenService tokens, string expectedAudience, out RequestContext context)
        => TryAuthenticate(metadata, tokens, expectedAudience, out context, out _);

    public static bool TryAuthenticate(IReadOnlyDictionary<string, string> metadata, SessionTokenService tokens, string expectedAudience, out RequestContext context, out DateTimeOffset expiresAt)
    {
        context = default!;
        expiresAt = default;
        if (!metadata.TryGetValue("x-v2-audience", out var audience) || !string.Equals(audience, expectedAudience, StringComparison.Ordinal)) return false;
        if (!metadata.TryGetValue("x-v2-session", out var token) || string.IsNullOrWhiteSpace(token)) return false;
        return tokens.TryValidate(token, expectedAudience, out context, out expiresAt);
    }
}
