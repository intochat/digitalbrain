using DigitalBrain.Core;
using PackContracts = DigitalBrain.Pack.Contracts;
using UiContracts = DigitalBrain.Ui.Contracts;

namespace DigitalBrain.Salesforce;

public static class SalesforceAuthSurfaces
{
    public static UiContracts.UiSurface CredentialForm(string emitter, string? clientId = null, string? message = null)
    {
        var children = new List<UiContracts.UiWidgetTree>
        {
            TextField(SalesforceClientFactory.ClientIdKey, "Connected App Client ID"),
            TextField(SalesforceClientFactory.ClientSecretKey, "Connected App Client Secret", secret: true),
            TextField(SalesforceClientFactory.UsernameKey, "Salesforce Username"),
            TextField(SalesforceClientFactory.PasswordKey, "Salesforce Password", secret: true),
            TextField(SalesforceClientFactory.SecurityTokenKey, "Security Token", secret: true),
            TextField(SalesforceClientFactory.LoginUrlKey, "Login URL (https://login.salesforce.com or sandbox)")
        };

        if (!string.IsNullOrWhiteSpace(message))
        {
            children.Insert(0, new UiContracts.UiWidgetTree(UiContracts.UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = message
            }));
        }

        var buttonProps = new Dictionary<string, object?>
        {
            ["label"] = "Login via Salesforce",
            ["synapseType"] = DigitalBrain.Core.SalesforceSignals.AuthRequested,
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            ["pack"] = SalesforceClientFactory.PackName
        };
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            buttonProps["clientId"] = clientId;
        }

        children.Add(new UiContracts.UiWidgetTree(UiContracts.UiKitVocabulary.Button, buttonProps));

        var tree = new UiContracts.UiWidgetTree(
            UiContracts.UiKitVocabulary.Screen,
            new Dictionary<string, object?> { ["title"] = SalesforceClientFactory.PackName + " configuration" },
            [
                new(UiContracts.UiKitVocabulary.Column, new Dictionary<string, object?>(), children)
            ]);

        var props = new Dictionary<string, object?>
        {
            [UiContracts.UiSurfaceKeys.SurfaceId] = "surface.pack-config." + SalesforceClientFactory.PackName.ToLowerInvariant(),
            [UiContracts.UiSurfaceKeys.Title] = SalesforceClientFactory.PackName + " configuration",
            [UiContracts.UiSurfaceKeys.RequiresInput] = true,
            [UiContracts.UiSurfaceKeys.Layout] = UiContracts.UiSurfaceLayouts.Panel,
            [UiContracts.UiSurfaceKeys.Emitter] = emitter,
            ["pack"] = SalesforceClientFactory.PackName,
            ["tree"] = tree
        };
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            props["clientId"] = clientId;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            props["message"] = message;
        }

        return new UiContracts.UiSurface(PackContracts.ConfigFormSurface.Kind, props);
    }

    private static UiContracts.UiWidgetTree TextField(string name, string label, bool secret = false)
    {
        var props = new Dictionary<string, object?>
        {
            ["label"] = label,
            ["key"] = name,
            ["name"] = name,
            ["placeholder"] = label
        };
        if (secret)
        {
            props["secret"] = true;
        }

        return new UiContracts.UiWidgetTree(UiContracts.UiKitVocabulary.TextField, props);
    }
}
