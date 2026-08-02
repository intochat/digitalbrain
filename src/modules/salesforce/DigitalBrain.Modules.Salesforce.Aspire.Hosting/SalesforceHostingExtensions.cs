using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Mcp.Aspire.Hosting;

namespace DigitalBrain.Salesforce.Aspire.Hosting;

public static class SalesforceHostingExtensions
{
    private static readonly McpProviderHostingDefinition Salesforce = new(
        "salesforce",
        "Salesforce",
        "salesforce",
        "DigitalBrain:Salesforce",
        "OAuth **consumer key (client ID)** from the Salesforce [External Client App](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/guide/create-external-client-app.html). DigitalBrain does **not** require a Salesforce client secret for this public-client style app.",
        ClientSecretDescription: null,
        "OAuth **callback URL** on the Salesforce External Client App (MCP public-client PKCE). Local `aspire run` defaults to "
        + $"`{LocalDevelopmentProductSurface.LocalDevelopmentOAuthCallbackUri}` "
        + $"(UI is fixed on port {LocalDevelopmentProductSurface.UiHttpPort}; path is `/oauth/callback`). "
        + "Register that exact URL once on the app. No `salesforce-client-secret` parameter exists.");

    public static DigitalBrainModuleBuilder<SalesforceModule> WithSalesforce(
        this DigitalBrainModuleBuilder<SalesforceModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);

        McpProviderHosting.Register(module, Salesforce);
        return module;
    }
}
