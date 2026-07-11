namespace DigitalBrain.Core;

public static class OAuthCallbackPaths
{
    public const string Google = "/oauth/callback/google";
    public const string Salesforce = "/oauth/callback/salesforce";
    public const string SalesforceStart = "/oauth/start/salesforce";

    public static bool IsAllowedSalesforceStartUrl(string? value) =>
        IsAllowedSalesforceStartUrl(value, expectedCallbackUrl: null);

    public static bool IsAllowedSalesforceStartUrl(string? value, string? expectedCallbackUrl)
    {
        if (!TryCreateSafeOAuthUrl(value, SalesforceStart, requireToken: true, out var start))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedCallbackUrl))
        {
            return start.IsLoopback;
        }

        return TryCreateSafeOAuthUrl(expectedCallbackUrl, Salesforce, requireToken: false, out var callback) &&
               Uri.Compare(
                   start,
                   callback,
                   UriComponents.SchemeAndServer,
                   UriFormat.Unescaped,
                   StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool TryCreateSafeOAuthUrl(
        string? value,
        string expectedPath,
        bool requireToken,
        out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!) ||
            !string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return false;
        }

        return requireToken
            ? uri.Query.StartsWith("?t=", StringComparison.Ordinal) &&
              uri.Query.Length is > 3 and <= 8192 &&
              !uri.Query.Contains('&')
            : string.IsNullOrEmpty(uri.Query);
    }
}
