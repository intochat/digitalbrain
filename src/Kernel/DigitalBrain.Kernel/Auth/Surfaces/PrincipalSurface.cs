using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal static class PrincipalSurface
{
    public static string InstanceName(PrincipalId principal, string surfaceName)
        => PrincipalScoped.InstanceName(principal, surfaceName);
}

