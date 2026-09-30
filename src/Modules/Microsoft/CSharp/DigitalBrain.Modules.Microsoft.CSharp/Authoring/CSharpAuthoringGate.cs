using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.CSharp;

internal static class CSharpAuthoringGate
{
    public const string DeveloperModeKey = "DigitalBrain:CSharpAuthoring:DeveloperMode";
    public const string LegacyDeveloperModeKey = "IntoChat:DeveloperMode";

    // Legacy IntoChat:DeveloperMode still gates authoring so existing deployments keep their off switch.
    public static bool IsOpen(IConfiguration configuration)
        => IsEnabled(configuration[DeveloperModeKey]) && IsEnabled(configuration[LegacyDeveloperModeKey]);

    // Absent means on; an explicit but unparseable value fails closed.
    private static bool IsEnabled(string? configured)
        => configured is null || (bool.TryParse(configured, out var enabled) && enabled);
}
