using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Ui;

using DigitalBrain.Ui.Contracts;
using DigitalBrain.Ui.Contracts.Ui;

// Bridges a raw widget-tree UiSurface (surface.Kind == WidgetTreeKind) into the RfwCard the Flutter
// WidgetTreeHost renders, carrying the experience/session markers a chat client needs to key its own replies.
public static class WidgetTreeRfwBridge
{
    public static bool TryCreate(UiSurface surface, string? addressedClientId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RfwCard? card)
    {
        if (surface.Kind != UiSurface.WidgetTreeKind || !surface.Props.TryGetValue("tree", out var treeObj))
        {
            card = null;
            return false;
        }

        var kind = surface.Props.TryGetValue("surfaceKind", out var surfaceKind) && surfaceKind is not null
            ? surfaceKind
            : surface.Kind;
        var payload = new Dictionary<string, object?> { ["tree"] = treeObj, ["kind"] = kind };
        // Carry experience markers so the experience host can match the hop and key its semantics on the surfaceId.
        // clientId/role let a chat client pick out its own assistant replies from the shared HomeFeed stream.
        foreach (var markerKey in new[] { "activeExperience", "experienceId", UiSurfaceKeys.SurfaceId, UiSurfaceKeys.Title, "clientId", "role", "surfaceKind" })
        {
            if (surface.Props.TryGetValue(markerKey, out var markerValue) && markerValue is not null)
            {
                payload[markerKey] = markerValue;
            }
        }
        var correlation = surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var sid) && sid is string sidStr && sidStr.Length > 0
            ? sidStr
            : surface.CorrelationId ?? surface.SynapseId;

        card = new RfwCard("digitalbrain", "WidgetTreeHost", JsonSerializer.Serialize(payload), addressedClientId)
        {
            CorrelationId = correlation
        };
        return true;
    }
}
