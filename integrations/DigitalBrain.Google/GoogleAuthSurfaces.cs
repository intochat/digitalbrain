using DigitalBrain.Core;
using PackContracts = DigitalBrain.Pack.Contracts;
using UiContracts = DigitalBrain.Ui.Contracts;

namespace DigitalBrain.Google;

public static class GoogleAuthSurfaces
{
    public static UiContracts.UiSurface CredentialForm(string emitter, string? clientId = null, string? message = null)
    {
        var children = new List<UiContracts.UiWidgetTree>
        {
            TextField(GoogleClientFactory.ClientIdKey, "Google Client ID"),
            TextField(GoogleClientFactory.ClientSecretKey, "Google Client Secret", secret: true)
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
            ["label"] = "Login via Google",
            ["synapseType"] = GoogleSignals.AuthRequested,
            ["callbackPath"] = GoogleClientFactory.DefaultCallbackPath,
            ["pack"] = GoogleClientFactory.PackName
        };
        if (!string.IsNullOrWhiteSpace(clientId))
            buttonProps["clientId"] = clientId;

        children.Add(new UiContracts.UiWidgetTree(UiContracts.UiKitVocabulary.Button, buttonProps));

        var tree = new UiContracts.UiWidgetTree(
            UiContracts.UiKitVocabulary.Screen,
            new Dictionary<string, object?> { ["title"] = GoogleClientFactory.PackName + " configuration" },
            new List<UiContracts.UiWidgetTree>
            {
                new(UiContracts.UiKitVocabulary.Column, new Dictionary<string, object?>(), children)
            });

        var props = new Dictionary<string, object?>
        {
            [UiContracts.UiSurfaceKeys.SurfaceId] = "surface.pack-config." + GoogleClientFactory.PackName.ToLowerInvariant(),
            [UiContracts.UiSurfaceKeys.Title] = GoogleClientFactory.PackName + " configuration",
            [UiContracts.UiSurfaceKeys.RequiresInput] = true,
            [UiContracts.UiSurfaceKeys.Layout] = UiContracts.UiSurfaceLayouts.Panel,
            [UiContracts.UiSurfaceKeys.Emitter] = emitter,
            ["pack"] = GoogleClientFactory.PackName,
            ["tree"] = tree
        };
        if (!string.IsNullOrWhiteSpace(clientId))
            props["clientId"] = clientId;
        if (!string.IsNullOrWhiteSpace(message))
            props["message"] = message;

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
            props["secret"] = true;

        return new UiContracts.UiWidgetTree(UiContracts.UiKitVocabulary.TextField, props);
    }
}
