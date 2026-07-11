using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Mcp;

public sealed class ToolActionPolicy(string? salesforceRedirectUri = null)
{
    private const int MaximumLabelCharacters = 64;
    private const int MaximumTargetCharacters = 4096;

    public bool IsAllowed(ToolAction? action) =>
        action is not null &&
        string.Equals(action.Kind, "openUrl", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(action.Label) &&
        action.Label.Length <= MaximumLabelCharacters &&
        !string.IsNullOrWhiteSpace(action.Target) &&
        action.Target.Length <= MaximumTargetCharacters &&
        (IsAllowedGoogleAuthorizationUrl(action.Target) ||
         OAuthCallbackPaths.IsAllowedSalesforceStartUrl(action.Target, salesforceRedirectUri));

    public bool IsAllowedOpenUrl(string label, string? target) =>
        target is not null && IsAllowed(new ToolAction("openUrl", label, target));

    private static bool IsAllowedGoogleAuthorizationUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort &&
        string.Equals(uri.AbsolutePath, "/o/oauth2/v2/auth", StringComparison.Ordinal) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment);
}
