using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;

namespace DigitalBrain.Google;

internal sealed class GmailLogins(GmailOAuthConfiguration configuration, IServiceProvider services)
    : BrowserLogins(LoginDefinition, services)
{
    internal const string ComposeScope = "compose";
    internal static readonly string[] ReadTools = ["gmail_get_current_account", "gmail_search_threads", "gmail_get_thread", "gmail_list_labels"];

    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "gmail",
        "Gmail",
        "GmailIntegration",
        "/integrations/gmail/login",
        "/integrations/gmail/callback",
        "Sign in with Google to connect Gmail. Credentials stay outside the conversation. Login never creates a draft.");

    protected override Uri? PublicOrigin => configuration.IsConfigured ? configuration.PublicOrigin : null;

    // A compose login escalates the scope and never resumes the interrupted read.
    internal UserActionRequest RequireLogin(bool compose, CancellationToken cancellationToken = default)
        => Require(compose ? [] : ReadTools, compose ? ComposeScope : null, cancellationToken);
}
