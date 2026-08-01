using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Mcp.Aspire.Hosting;

namespace DigitalBrain.Google.Aspire.Hosting;

public static class GoogleHostingExtensions
{
    private static readonly McpProviderHostingDefinition Gmail = new(
        "google.gmail",
        "Gmail",
        "google",
        "DigitalBrain:Google:Gmail",
        "OAuth **client ID** from [Google Auth Platform clients](https://console.cloud.google.com/auth/clients). Create a **Web application** client. For local Aspire you only paste this ID and the client secret; redirect is defaulted.",
        "OAuth **client secret** for that same Google Web client. Never commit it; Aspire persists it as a secret parameter.",
        "OAuth **redirect URI** registered on the Google client. Local `aspire run` defaults to "
        + $"`{LocalDevelopmentProductSurface.LocalDevelopmentOAuthCallbackUri}` "
        + $"(UI is fixed on port {LocalDevelopmentProductSurface.UiHttpPort}). "
        + "Register that exact URI once under Authorized redirect URIs. Override only if your UI is not on that port.");

    public static DigitalBrainModuleBuilder<GoogleModule> WithGmail(this DigitalBrainModuleBuilder<GoogleModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);

        McpProviderHosting.Register(module, Gmail);
        return module;
    }
}
