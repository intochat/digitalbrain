namespace DigitalBrain.Core;

public static class OAuthCallbackPaths
{
    public const string GoogleProvider = "google";
    public const string SalesforceProvider = "salesforce";
    public const string Google = "/oauth/callback/google";
    public const string Salesforce = "/oauth/callback/salesforce";
    public const string GoogleStart = "/oauth/start/google";
    public const string SalesforceStart = "/oauth/start/salesforce";
    public const int MinimumFlowReferenceLength = 32;
    public const int MaximumFlowReferenceLength = 1024;

    public static bool IsSupportedProvider(string? provider) =>
        provider is GoogleProvider or SalesforceProvider;

    public static string CreateInternalStartPath(string provider, string flowReference)
    {
        if (!IsOpaqueFlowReference(flowReference))
            throw new ArgumentException("An opaque bounded OAuth flow reference is required.", nameof(flowReference));
        return StartPath(provider) + "?f=" + flowReference;
    }

    public static bool TryParseInternalStartPath(
        string? value,
        string expectedProvider,
        out string flowReference)
    {
        flowReference = string.Empty;
        string expectedPath;
        try
        {
            expectedPath = StartPath(expectedProvider);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var prefix = expectedPath + "?f=";
        if (value is null || value.Length > prefix.Length + MaximumFlowReferenceLength ||
            !value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var candidate = value[prefix.Length..];
        if (!IsOpaqueFlowReference(candidate)) return false;
        flowReference = candidate;
        return true;
    }

    public static bool TryParseInternalStartPath(
        string? value,
        out string provider,
        out string flowReference)
    {
        if (TryParseInternalStartPath(value, GoogleProvider, out flowReference))
        {
            provider = GoogleProvider;
            return true;
        }
        if (TryParseInternalStartPath(value, SalesforceProvider, out flowReference))
        {
            provider = SalesforceProvider;
            return true;
        }
        provider = string.Empty;
        flowReference = string.Empty;
        return false;
    }

    public static bool IsOpaqueFlowReference(string? value) =>
        value is { Length: >= MinimumFlowReferenceLength and <= MaximumFlowReferenceLength } &&
        value.All(static character => character is
            >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or
            '-' or '_');

    public static bool IsAllowedProviderAuthorizationUrl(string provider, string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        return provider switch
        {
            GoogleProvider =>
                string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(uri.AbsolutePath, "/o/oauth2/v2/auth", StringComparison.Ordinal),
            SalesforceProvider =>
                IsSalesforceAuthorizationHost(uri.Host) &&
                string.Equals(uri.AbsolutePath, "/services/oauth2/authorize", StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool IsSalesforceAuthorizationHost(string host) =>
        host.EndsWith(".salesforce.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".site.com", StringComparison.OrdinalIgnoreCase);

    private static string StartPath(string provider) => provider switch
    {
        GoogleProvider => GoogleStart,
        SalesforceProvider => SalesforceStart,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported OAuth provider.")
    };
}
