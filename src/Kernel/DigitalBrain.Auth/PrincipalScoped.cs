using DigitalBrain.Abstractions;

namespace DigitalBrain.Auth;

// Host-side alias for PrincipalPartition — chat/surface map authenticated principals here.
public static class PrincipalScoped
{
    public static string InstanceName(PrincipalId principal, string localName)
        => PrincipalPartition.InstanceName(principal, localName);
}

