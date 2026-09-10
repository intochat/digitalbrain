using DigitalBrain.Core;

namespace DigitalBrain.Google;

internal sealed class GmailLogins(GmailOAuthConfiguration configuration)
    : BrowserLogins(LoginDefinition)
{
    internal const string ComposeScope = "compose";

    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "gmail",
        "Gmail",
        "GmailIntegration",
        "/integrations/gmail/login",
        "/integrations/gmail/callback",
        "Sign in with Google to connect Gmail. Credentials stay outside the conversation. Login never creates a draft.");

    protected override Uri? PublicOrigin => configuration.IsConfigured ? configuration.PublicOrigin : null;
}
