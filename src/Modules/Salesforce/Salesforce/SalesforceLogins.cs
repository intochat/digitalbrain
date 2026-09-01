using DigitalBrain.Abstractions.Interactions;
using DigitalBrain.Sdk;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceLogins(SalesforceOAuthConfiguration configuration, IServiceProvider services)
    : BrowserLogins(LoginDefinition, services)
{
    internal static readonly string[] ReadTools = ["salesforce_get_current_user", "salesforce_soql_query"];

    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "salesforce",
        "Salesforce",
        "SalesforceIntegration",
        "/integrations/salesforce/login",
        "/integrations/salesforce/callback",
        "Log in to Salesforce to continue this request. Your credentials stay outside the conversation.");

    protected override Uri? PublicOrigin => configuration.PublicOrigin;

    // A read resumes after login; a write needs a fresh preview and explicit confirmation.
    internal UserActionRequest RequireLogin(bool readOnly, CancellationToken cancellationToken = default)
        => Require(readOnly ? ReadTools : [], null, cancellationToken);
}
