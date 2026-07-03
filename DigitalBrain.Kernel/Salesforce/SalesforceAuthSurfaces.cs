using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Kernel.Salesforce;

internal static class SalesforceAuthSurfaces
{
    private static readonly PackConfigField[] Fields =
    [
        new(SalesforceClientFactory.ClientIdKey, "Connected App Client ID", PackConfigFieldKind.Text),
        new(SalesforceClientFactory.ClientSecretKey, "Connected App Client Secret", PackConfigFieldKind.Secret),
        new(SalesforceClientFactory.UsernameKey, "Salesforce Username", PackConfigFieldKind.Text),
        new(SalesforceClientFactory.PasswordKey, "Salesforce Password", PackConfigFieldKind.Secret),
        new(SalesforceClientFactory.SecurityTokenKey, "Security Token", PackConfigFieldKind.Secret),
        new(SalesforceClientFactory.LoginUrlKey, "Login URL (https://login.salesforce.com or sandbox)", PackConfigFieldKind.Text),
        new(SalesforceClientFactory.ApiVersionKey, "API Version (optional, e.g. v60.0)", PackConfigFieldKind.Text)
    ];

    public static UiSurface CredentialForm(string emitter, string? sessionId = null)
    {
        var surface = ConfigFormSurface.Build(SalesforceClientFactory.PackName, Fields, emitter);
        var props = new Dictionary<string, object?>(surface.Props)
        {
            [UiSurfaceKeys.Title] = "Salesforce credentials",
            ["role"] = "assistant"
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
            props["sessionId"] = sessionId;

        return surface with { Props = props };
    }
}
