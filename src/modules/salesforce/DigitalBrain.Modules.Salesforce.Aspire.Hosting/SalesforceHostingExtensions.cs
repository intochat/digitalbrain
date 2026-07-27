using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Integrations.Mcp.Aspire.Hosting;

namespace DigitalBrain.Salesforce.Aspire.Hosting;

public static class SalesforceHostingExtensions
{
    private static readonly McpProviderHostingDefinition Salesforce = new(
        "salesforce",
        "Salesforce",
        "salesforce",
        "DigitalBrain:Salesforce",
        "Consumer key from the Salesforce [External Client App](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/guide/create-external-client-app.html).",
        ClientSecretDescription: null,
        "OAuth callback URI registered on the Salesforce External Client App. Use an HTTP loopback callback only with the explicit local development authorization mode.");

    public static DigitalBrainModuleBuilder<SalesforceModule> WithSalesforce(
        this DigitalBrainModuleBuilder<SalesforceModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);

        McpProviderHosting.Register(module, Salesforce);
        return module;
    }
}
