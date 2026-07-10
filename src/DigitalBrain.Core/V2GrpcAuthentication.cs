namespace DigitalBrain.Core.V2;

public static class V2GrpcAuthentication
{
    public static bool TryAuthenticate(IReadOnlyDictionary<string, string> metadata, V2SessionTokenService tokens, string expectedAudience, out RequestContext context)
    {
        context = default!;
        if (!metadata.TryGetValue("x-v2-audience", out var audience) || !string.Equals(audience, expectedAudience, StringComparison.Ordinal)) return false;
        if (!metadata.TryGetValue("x-v2-session", out var token) || string.IsNullOrWhiteSpace(token)) return false;
        return tokens.TryValidate(token, out context);
    }
}
