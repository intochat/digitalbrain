using DigitalBrain.Abstractions;

namespace DigitalBrain.Auth;

public static class PrincipalSurface
{
    public static string InstanceName(PrincipalId principal, string surfaceName)
        => PrincipalScoped.InstanceName(principal, surfaceName);
}

