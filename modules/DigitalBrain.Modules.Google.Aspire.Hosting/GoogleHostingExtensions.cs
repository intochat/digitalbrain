using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Integrations.Mcp.Aspire.Hosting;

namespace DigitalBrain.Google.Aspire.Hosting;

public static class GoogleHostingExtensions
{
    private static readonly McpProviderHostingDefinition Gmail = new(
        "google.gmail",
        "Gmail",
        "google",
        "DigitalBrain:Google:Gmail",
        "OAuth client ID from [Google Auth Platform](https://console.cloud.google.com/auth/clients).",
        "OAuth client secret from [Google Auth Platform](https://console.cloud.google.com/auth/clients).",
        "OAuth callback URI registered on the Google client. Use an HTTP loopback callback only with the explicit local development authorization mode.");

    public static DigitalBrainModuleBuilder<GoogleModule> WithGmail(
        this DigitalBrainModuleBuilder<GoogleModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);

        McpProviderHosting.Register(module, Gmail);
        return module;
    }
}
