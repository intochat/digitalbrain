namespace DigitalBrain.Abstractions;

public static class OAuthCallbackPaths
{
    public const string RelativePath = "/oauth/callback";

    public static bool EndsWithCanonicalCallback(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        return redirectUri.AbsolutePath.EndsWith(RelativePath, StringComparison.OrdinalIgnoreCase);
    }
}
