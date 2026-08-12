using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

// Host-side alias for PrincipalPartition — chat/surface map authenticated principals here.
internal static class PrincipalScoped
{
    public static string InstanceName(PrincipalId principal, string localName)
        => PrincipalPartition.InstanceName(principal, localName);
}

internal static class PrincipalChat
{
    public static string InstanceName(PrincipalId principal, string conversationName)
        => PrincipalScoped.InstanceName(principal, conversationName);
}

internal static class PrincipalSurface
{
    public static string InstanceName(PrincipalId principal, string surfaceName)
        => PrincipalScoped.InstanceName(principal, surfaceName);
}
