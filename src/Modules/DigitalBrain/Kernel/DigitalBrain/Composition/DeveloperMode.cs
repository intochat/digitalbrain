using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Core;

public static class DeveloperMode
{
    public const string Key = "DigitalBrain:DeveloperMode";

    // Absent means on for the local owner; an explicit but unparseable value fails closed.
    public static bool IsEnabled(IConfiguration configuration)
        => configuration[Key] is not { } configured || (bool.TryParse(configured, out var enabled) && enabled);
}
