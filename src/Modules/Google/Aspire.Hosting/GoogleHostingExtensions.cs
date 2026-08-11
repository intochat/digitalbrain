using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Google.Aspire.Hosting;

public static class GoogleHostingExtensions
{
    private static readonly OAuthProviderHostingDefinition Gmail = new(
        GoogleModule.GmailServerKey,
        GoogleModule.GmailDisplayName,
        "google",
        GoogleModule.GmailConfigurationRoot,
        "OAuth **client ID** from [Google Auth Platform clients](https://console.cloud.google.com/auth/clients). Create a **Web application** client for the official Gmail MCP server. For local Aspire you only paste this ID and the client secret; redirect is defaulted.",
        "OAuth **client secret** for that same Google Web application client. Never commit it; Aspire persists it as a secret parameter.",
        "OAuth **redirect URI** registered on the Google Web application client under Authorized redirect URIs. "
        + "Use the **exact same URL** DigitalBrain serves (local default ends with `/oauth/callback`).");

    public static DigitalBrainModuleBuilder<GoogleModule> WithGmail(this DigitalBrainModuleBuilder<GoogleModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);

        OAuthProviderHosting.Register(module, Gmail);
        return module;
    }
}
