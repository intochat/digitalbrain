using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Kernel.Salesforce;

using DigitalBrain.Pack.Contracts;

using DigitalBrain.Ui.Contracts;

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

    public static UiSurface CredentialForm(string emitter, string? clientId = null, string? message = null)
    {
        var surface = ConfigFormSurface.Build(SalesforceClientFactory.PackName, Fields, emitter);
        var tree = AssertTree(surface.Props["tree"]);
        var content = tree.Children?.FirstOrDefault();
        if (content is not null)
        {
            var children = new List<UiWidgetTree>();
            if (!string.IsNullOrWhiteSpace(message))
            {
                children.Add(new(UiKitVocabulary.Text, new Dictionary<string, object?>
                {
                    ["text"] = message.Trim(),
                    ["role"] = "alert"
                }));
            }

            var oauthButtonProps = new Dictionary<string, object?>
            {
                ["label"] = "Login via Salesforce",
                ["icon"] = "salesforce",
                ["eventName"] = SalesforceSignals.AuthRequested,
                ["synapseType"] = SalesforceSignals.AuthRequested,
                ["pack"] = SalesforceClientFactory.PackName,
                ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath
            };
            if (!string.IsNullOrWhiteSpace(clientId))
                oauthButtonProps["clientId"] = clientId;

            children.Add(new(UiKitVocabulary.Button, oauthButtonProps));
            children.Add(new(UiKitVocabulary.Divider, new Dictionary<string, object?>()));
            children.AddRange(content.Children ?? []);

            tree = tree with
            {
                Children =
                [
                    content with { Children = children }
                ]
            };
        }

        var props = new Dictionary<string, object?>(surface.Props)
        {
            [UiSurfaceKeys.Title] = "Salesforce credentials",
            ["role"] = "assistant",
            ["tree"] = tree
        };

        if (!string.IsNullOrWhiteSpace(clientId))
            props["clientId"] = clientId;

        return surface with { Props = props };
    }

    private static UiWidgetTree AssertTree(object? value) =>
        value as UiWidgetTree
        ?? throw new InvalidOperationException("Salesforce credential form did not contain a widget tree.");
}
