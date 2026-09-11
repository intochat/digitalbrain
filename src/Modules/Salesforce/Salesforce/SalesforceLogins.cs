using DigitalBrain.Core;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceLogins(SalesforceOAuthConfiguration configuration)
    : BrowserLogins(LoginDefinition)
{
    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "salesforce",
        "Salesforce",
        "SalesforceIntegration",
        "/integrations/salesforce/login",
        "/integrations/salesforce/callback",
        "Log in to Salesforce to continue this request. Your credentials stay outside the conversation.");

    protected override Uri? PublicOrigin => configuration.PublicOrigin;
}
